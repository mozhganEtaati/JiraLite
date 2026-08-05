<#
.SYNOPSIS
    Seeds JiraLite with demo data: a workspace of five people, three teams,
    three projects, and 33 realistic issues with comments, sprints and labels.

.DESCRIPTION
    Everything goes through the public HTTP API, so the data is exactly what a
    real team would have produced - activity log entries, notifications, board
    ranks and validation all happen for real. Nothing is inserted directly.

    The one exception is invitation tokens, which the API deliberately never
    returns (they are credentials), so those are read from SQL Server.

    Re-running reuses the same Organization and Workspace by name and replaces
    the projects inside it. It deliberately does not create a new Workspace per
    run: there is no delete-Workspace endpoint and archiving is irreversible, so
    that would leave a growing pile of empty Workspaces - and the web client
    selects workspaces[0] when nothing is remembered, which would land the demo
    on an empty one. If clearing the old projects fails the script warns rather
    than quietly leaving duplicates; `docker compose down -v` gives a clean slate.

.EXAMPLE
    pwsh ./scripts/seed-demo.ps1
    pwsh ./scripts/seed-demo.ps1 -ApiBase http://localhost:8080
#>

[CmdletBinding()]
param(
    [string]$ApiBase = "http://localhost:8080",
    [string]$SqlContainer = "jiralite-sqlserver-1",
    [string]$SaPassword = "Dev_OnlyPassw0rd!",
    [string]$DemoEmail = "demo@jiralite.dev",
    [string]$DemoPassword = "Demo_Passw0rd1"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# ---------- plumbing ----------

function Invoke-Api {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        $Body,
        [string]$Token
    )
    $headers = @{}
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }

    $args = @{
        Method      = $Method
        Uri         = "$ApiBase$Path"
        Headers     = $headers
        ContentType = "application/json"
    }
    if ($null -ne $Body) {
        $args.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
    }

    # /api/auth/* allows 10 requests per minute per IP (spec/01-authentication.md
    # NFR-04). Registering five accounts needs more than that, so a 429 is an
    # expected part of a cold run, not a failure - wait out the window and retry.
    for ($attempt = 1; ; $attempt++) {
        try {
            return Invoke-RestMethod @args
        } catch {
            $response = $_.Exception.Response
            $status = 0
            if ($response) { $status = [int]$response.StatusCode }

            if ($status -eq 429 -and $attempt -le 5) {
                $wait = 0
                if ($response.Headers) {
                    # Windows PowerShell exposes headers as a NameValueCollection,
                    # PowerShell 7 as HttpResponseHeaders. Try both.
                    try { $wait = [int]$response.Headers["Retry-After"] } catch { }
                    if (-not $wait) {
                        try { $wait = [int]$response.Headers.RetryAfter.Delta.TotalSeconds } catch { }
                    }
                }
                if ($wait -le 0) { $wait = 60 }
                $wait++  # clear the window boundary rather than land exactly on it

                Write-Host "  rate limited on $Path - waiting ${wait}s" -ForegroundColor DarkYellow
                Start-Sleep -Seconds $wait
                continue
            }

            # Surface the RFC 7807 body: "(400) Bad Request" on its own says nothing
            # about which field the API objected to.
            $detail = ""
            if ($response) {
                $stream = $response.GetResponseStream()
                $stream.Position = 0
                $detail = (New-Object System.IO.StreamReader($stream)).ReadToEnd()
            }
            throw "$Method $Path failed: $($_.Exception.Message)`n$detail"
        }
    }
}

function New-DemoUser {
    param([string]$Email, [string]$Password = "Demo_Passw0rd1")

    # Log in first and register only if that fails, so a re-run against a
    # database that already has these accounts is a no-op rather than a 409.
    $auth = $null
    try {
        $auth = Invoke-Api POST "/api/auth/login" @{ email = $Email; password = $Password }
    } catch {
        Invoke-Api POST "/api/auth/register" @{ email = $Email; password = $Password } | Out-Null
        $auth = Invoke-Api POST "/api/auth/login" @{ email = $Email; password = $Password }
    }
    $me = Invoke-Api GET "/api/users/me" -Token $auth.accessToken

    [pscustomobject]@{
        Email = $Email
        Token = $auth.accessToken
        Id    = $me.id
    }
}

# The API never returns an invitation token, so this is the one place the seed
# reaches past it. Demo-stack only - it assumes the Compose SQL Server.
function Get-InvitationToken {
    param([string]$WorkspaceId, [string]$Email)

    $sql = "SET NOCOUNT ON; SELECT TOP 1 Token FROM Invitation WHERE WorkspaceId = '$WorkspaceId' AND Email = '$Email' ORDER BY CreatedAtUtc DESC;"
    $raw = docker exec $SqlContainer /opt/mssql-tools18/bin/sqlcmd `
        -S localhost -U sa -P $SaPassword -C -d JiraLite -h -1 -W -Q $sql
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed reading the invitation token for $Email" }
    ($raw | Where-Object { $_ -and $_.Trim() } | Select-Object -First 1).Trim()
}

function Add-WorkspaceMember {
    param($Admin, [string]$WorkspaceId, $Member, [string]$WorkspaceRole = "Member")

    # The Workspace is reused across runs, so most of the time everyone is already in it.
    $existing = (Invoke-Api GET "/api/workspaces/$WorkspaceId/members" -Token $Admin.Token).items
    if ($existing | Where-Object { $_.userId -eq $Member.Id }) { return $false }

    Invoke-Api POST "/api/workspaces/$WorkspaceId/invitations" `
        @{ email = $Member.Email; role = $WorkspaceRole } -Token $Admin.Token | Out-Null

    $token = Get-InvitationToken -WorkspaceId $WorkspaceId -Email $Member.Email
    Invoke-Api POST "/api/invitations/$token/accept" -Token $Member.Token | Out-Null
    return $true
}

function Get-Board {
    param($User, [string]$ProjectId)

    $boards = Invoke-Api GET "/api/projects/$ProjectId/boards" -Token $User.Token
    $board = Invoke-Api GET "/api/boards/$($boards.items[0].id)" -Token $User.Token

    [pscustomobject]@{
        Id         = $board.id
        ToDo       = ($board.columns | Where-Object { $_.isDefault }).id
        InProgress = ($board.columns | Where-Object { -not $_.isDefault -and -not $_.isDoneColumn }).id
        Done       = ($board.columns | Where-Object { $_.isDoneColumn }).id
    }
}

function Move-Issue {
    param($User, [string]$IssueId, [string]$ColumnId)

    $issue = Invoke-Api GET "/api/issues/$IssueId" -Token $User.Token
    Invoke-Api PATCH "/api/issues/$IssueId/move" `
        @{ boardColumnId = $ColumnId; rowVersion = $issue.rowVersion } -Token $User.Token | Out-Null
}

function Write-Step { param([string]$Message) Write-Host "  $Message" -ForegroundColor DarkGray }

# ---------- the cast ----------

Write-Host "`nSeeding JiraLite demo data at $ApiBase" -ForegroundColor Cyan
Write-Host "Team" -ForegroundColor White

$demo  = New-DemoUser -Email $DemoEmail -Password $DemoPassword
$alice = New-DemoUser -Email "alice@jiralite.dev"
$ben   = New-DemoUser -Email "ben@jiralite.dev"
$chloe = New-DemoUser -Email "chloe@jiralite.dev"
$diego = New-DemoUser -Email "diego@jiralite.dev"
$team  = @($alice, $ben, $chloe, $diego)

foreach ($person in @($demo) + $team) { Write-Step $person.Email }

# Display names make the activity feed and assignee chips read like a real team.
$names = @{
    $demo.Email  = "Demo User"
    $alice.Email = "Alice Rahimi"
    $ben.Email   = "Ben Okafor"
    $chloe.Email = "Chloe Martin"
    $diego.Email = "Diego Santos"
}
foreach ($person in @($demo) + $team) {
    Invoke-Api PATCH "/api/users/me" @{ displayName = $names[$person.Email] } -Token $person.Token | Out-Null
}

# ---------- workspace ----------

Write-Host "Workspace" -ForegroundColor White

# Clear out anything a previous run left behind, so the dashboard shows one
# coherent workspace rather than an accumulating pile. Only ever touches
# projects the demo account is a member of.
#
# A project must be archived before it can be deleted, so this is two calls per
# project, not one. Workspaces are deliberately left alone: archiving one is
# irreversible (there is no unarchive endpoint) and an archived workspace is
# read-only, which would lock its projects in place permanently.
$stale = Invoke-Api GET "/api/dashboard/my-projects" -Token $demo.Token
$cleared = 0
$stuck = 0
foreach ($old in $stale.items) {
    try {
        Invoke-Api POST "/api/projects/$($old.id)/archive" -Token $demo.Token | Out-Null
        Invoke-Api DELETE "/api/projects/$($old.id)" -Token $demo.Token | Out-Null
        $cleared++
    } catch {
        $stuck++
    }
}
if ($cleared -gt 0) { Write-Step "cleared $cleared project(s) from a previous run" }

# Never fail silently here: a project left behind still shows on My Projects
# (spec/14-dashboard.md BR-03 scopes it to membership, not archive state), so
# the demo would quietly show duplicates of every project.
if ($stuck -gt 0) {
    Write-Host "  WARNING: $stuck project(s) from a previous run could not be removed." -ForegroundColor Yellow
    Write-Host "  They stay on My Projects alongside the new ones. Reset the stack for a clean demo:" -ForegroundColor Yellow
    Write-Host "    docker compose down -v; docker compose up -d" -ForegroundColor Yellow
}

# Reuse the Organization and Workspace by name instead of stamping a new one each run.
# There is no delete-Workspace endpoint and archiving is irreversible, so a fresh Workspace
# per run piles up empty leftovers - and the web client selects workspaces[0] when nothing is
# remembered (web/components/shell.tsx), which lands the demo on an empty one.
$orgName = "Acme Product Group"
$workspaceName = "Product Engineering"

$org = (Invoke-Api GET "/api/organizations" -Token $demo.Token).items | Where-Object { $_.name -eq $orgName } | Select-Object -First 1
if (-not $org) {
    $org = Invoke-Api POST "/api/organizations" @{ name = $orgName } -Token $demo.Token
}

$workspace = (Invoke-Api GET "/api/workspaces" -Token $demo.Token).items | Where-Object { $_.name -eq $workspaceName } | Select-Object -First 1
if (-not $workspace) {
    $workspace = Invoke-Api POST "/api/organizations/$($org.id)/workspaces" `
        @{ name = $workspaceName } -Token $demo.Token
    Write-Step "$($workspace.name) (created)"
} else {
    Write-Step "$($workspace.name) (reused)"
}
$workspaceId = $workspace.id

$joined = 0
foreach ($person in $team) {
    if (Add-WorkspaceMember -Admin $demo -WorkspaceId $workspaceId -Member $person) { $joined++ }
}
Write-Step "$($team.Count) members in the workspace ($joined newly invited)"

# ---------- teams ----------

Write-Host "Teams" -ForegroundColor White

# Teams are Workspace-scoped and independent of Project membership (spec/04-teams.md), so they
# are seeded separately rather than derived from the Project rosters below.
$teamSpecs = @(
    @{ Name = "Platform";  Description = "Backend services, APIs and infrastructure."; Lead = $alice; Members = @($alice, $ben) }
    @{ Name = "Mobile";    Description = "iOS and Android clients.";                   Lead = $ben;   Members = @($ben, $chloe) }
    @{ Name = "Design";    Description = "Design system, UX research and accessibility."; Lead = $chloe; Members = @($chloe, $diego, $demo) }
)

$existingTeams = (Invoke-Api GET "/api/workspaces/$workspaceId/teams" -Token $demo.Token).items
foreach ($spec in $teamSpecs) {
    $team_ = $existingTeams | Where-Object { $_.name -eq $spec.Name } | Select-Object -First 1
    if (-not $team_) {
        $team_ = Invoke-Api POST "/api/workspaces/$workspaceId/teams" `
            @{ name = $spec.Name; description = $spec.Description } -Token $demo.Token
    }

    foreach ($person in $spec.Members) {
        try {
            Invoke-Api POST "/api/teams/$($team_.id)/members" @{ userId = $person.Id } -Token $demo.Token | Out-Null
        } catch { }  # already a member on a re-run
    }
    Invoke-Api PATCH "/api/teams/$($team_.id)/members/$($spec.Lead.Id)" `
        @{ isLead = $true } -Token $demo.Token | Out-Null

    Write-Step "$($spec.Name) - $($spec.Members.Count) members, lead $($names[$spec.Lead.Email])"
}

# ---------- projects ----------

Write-Host "Projects" -ForegroundColor White

$projectSpecs = @(
    @{ Key = "APOLLO"; Name = "Apollo Web Platform"; Description = "Customer-facing web application and public API." }
    @{ Key = "ORBIT";  Name = "Orbit Mobile";        Description = "iOS and Android clients sharing a React Native core." }
    @{ Key = "NOVA";   Name = "Nova Design System";  Description = "Shared component library, tokens and documentation." }
)

$projects = @()
foreach ($spec in $projectSpecs) {
    $project = Invoke-Api POST "/api/workspaces/$workspaceId/projects" `
        @{ key = $spec.Key; name = $spec.Name; description = $spec.Description } -Token $demo.Token

    $board = Get-Board -User $demo -ProjectId $project.id

    # The auto-created board is Kanban, and sprints only exist on Scrum boards,
    # so each project gets a second board to plan on. Issues stay on the Kanban
    # columns; sprint membership is project-scoped, not board-scoped.
    $scrumBoard = Invoke-Api POST "/api/projects/$($project.id)/boards" `
        @{ name = "Sprint Board"; type = "Scrum" } -Token $demo.Token

    # Roles vary per project so the dashboard shows a realistic spread rather
    # than the same badge three times.
    $roles = switch ($spec.Key) {
        "APOLLO" { @{ $alice.Id = "Developer"; $ben.Id = "Developer"; $chloe.Id = "Developer" } }
        "ORBIT"  { @{ $ben.Id = "Developer"; $chloe.Id = "Developer"; $diego.Id = "Viewer" } }
        "NOVA"   { @{ $alice.Id = "Developer"; $diego.Id = "Viewer" } }
    }
    foreach ($userId in $roles.Keys) {
        Invoke-Api POST "/api/projects/$($project.id)/members" `
            @{ userId = $userId; role = $roles[$userId] } -Token $demo.Token | Out-Null
    }

    $projects += [pscustomobject]@{
        Id = $project.id; Key = $spec.Key; Name = $spec.Name; Board = $board
        ScrumBoardId = $scrumBoard.id; MemberIds = @($roles.Keys)
    }
    Write-Step "$($spec.Key) - $($spec.Name) ($($roles.Count + 1) members)"
}

# ---------- labels ----------

Write-Host "Labels" -ForegroundColor White

$labelSpecs = @(
    @{ Name = "frontend";  Color = "#3B82F6" }
    @{ Name = "backend";   Color = "#8B5CF6" }
    @{ Name = "tech-debt"; Color = "#F59E0B" }
    @{ Name = "ux";        Color = "#EC4899" }
    @{ Name = "security";  Color = "#EF4444" }
)

$labels = @{}
foreach ($project in $projects) {
    $labels[$project.Key] = @()
    foreach ($spec in $labelSpecs) {
        $label = Invoke-Api POST "/api/projects/$($project.Id)/labels" `
            @{ name = $spec.Name; color = $spec.Color } -Token $demo.Token
        $labels[$project.Key] += $label.id
    }
}
Write-Step "$($labelSpecs.Count) labels on each project"

# ---------- sprints ----------

Write-Host "Sprints" -ForegroundColor White

$today = Get-Date
$sprints = @{}
foreach ($project in $projects) {
    $current = Invoke-Api POST "/api/boards/$($project.ScrumBoardId)/sprints" @{
        name                  = "$($project.Key) Sprint 14"
        goal                  = "Ship the checkout rewrite and close out the accessibility audit."
        plannedStartDateUtc   = $today.AddDays(-4).ToString("yyyy-MM-dd")
        plannedEndDateUtc     = $today.AddDays(10).ToString("yyyy-MM-dd")
    } -Token $demo.Token

    $next = Invoke-Api POST "/api/boards/$($project.ScrumBoardId)/sprints" @{
        name                  = "$($project.Key) Sprint 15"
        goal                  = "Performance pass and mobile polish."
        plannedStartDateUtc   = $today.AddDays(11).ToString("yyyy-MM-dd")
        plannedEndDateUtc     = $today.AddDays(25).ToString("yyyy-MM-dd")
    } -Token $demo.Token

    try {
        Invoke-Api POST "/api/sprints/$($current.id)/start" -Token $demo.Token | Out-Null
        Write-Step "$($project.Key): Sprint 14 active, Sprint 15 planned"
    } catch {
        Write-Step "$($project.Key): sprints created (start skipped: $($_.Exception.Message))"
    }

    $sprints[$project.Key] = [pscustomobject]@{ Current = $current.id; Next = $next.id }
}

# ---------- issues ----------

Write-Host "Issues" -ForegroundColor White

# Titles that read like a real backlog. Each entry is type/title/priority, and
# `Mine` marks the ones assigned to the demo account so My Tasks is full.
$backlog = @{
    "APOLLO" = @(
        @{ Type = "Epic";  Title = "Checkout rewrite";                                   Priority = "Critical"; Mine = $true;  Due = -2 }
        @{ Type = "Story"; Title = "Guest checkout without an account";                  Priority = "High";    Mine = $true;  Due = 1 }
        @{ Type = "Story"; Title = "Save payment methods for returning customers";       Priority = "High";    Mine = $false; Due = 5 }
        @{ Type = "Bug";   Title = "Cart total ignores promo code on first apply";       Priority = "Critical"; Mine = $true;  Due = -1 }
        @{ Type = "Bug";   Title = "Session expires mid-checkout on slow connections";   Priority = "High";    Mine = $true;  Due = 0 }
        @{ Type = "Task";  Title = "Add Stripe webhook retry handling";                  Priority = "Medium";  Mine = $false; Due = 7 }
        @{ Type = "Task";  Title = "Backfill order records missing a currency code";     Priority = "Low";     Mine = $true;  Due = 12 }
        @{ Type = "Story"; Title = "Order confirmation email template";                  Priority = "Medium";  Mine = $false; Due = 9 }
        @{ Type = "Bug";   Title = "Address autocomplete drops apartment numbers";       Priority = "Medium";  Mine = $true;  Due = 3 }
        @{ Type = "Task";  Title = "Rate-limit the public price quote endpoint";         Priority = "High";    Mine = $false; Due = 4 }
        @{ Type = "Story"; Title = "Refund flow for partially shipped orders";           Priority = "Medium";  Mine = $false; Due = 18 }
        @{ Type = "Task";  Title = "Upgrade to .NET 10 and retire the shim package";     Priority = "Low";     Mine = $true;  Due = 21 }
        @{ Type = "Bug";   Title = "Duplicate charge when the user double-taps Pay";     Priority = "Critical"; Mine = $true;  Due = -5 }
        @{ Type = "Task";  Title = "Add integration tests for the tax calculator";       Priority = "Medium";  Mine = $false; Due = $null }
        @{ Type = "Story"; Title = "Show estimated delivery date at checkout";           Priority = "Low";     Mine = $false; Due = $null }
    )
    "ORBIT" = @(
        @{ Type = "Epic";  Title = "Offline-first sync";                                 Priority = "High";    Mine = $true;  Due = 14 }
        @{ Type = "Story"; Title = "Queue writes while the device is offline";           Priority = "High";    Mine = $true;  Due = 2 }
        @{ Type = "Bug";   Title = "Push notifications silently fail on Android 15";     Priority = "Critical"; Mine = $true;  Due = -3 }
        @{ Type = "Bug";   Title = "Pull-to-refresh spinner never dismisses";            Priority = "Medium";  Mine = $false; Due = 6 }
        @{ Type = "Task";  Title = "Reduce cold start below 2 seconds";                  Priority = "High";    Mine = $true;  Due = 8 }
        @{ Type = "Story"; Title = "Biometric unlock on returning to the app";           Priority = "Medium";  Mine = $false; Due = 11 }
        @{ Type = "Task";  Title = "Migrate to the new navigation library";              Priority = "Low";     Mine = $false; Due = 20 }
        @{ Type = "Bug";   Title = "Deep links open a blank screen from cold start";     Priority = "High";    Mine = $true;  Due = 0 }
        @{ Type = "Story"; Title = "Dark mode for the settings screens";                 Priority = "Low";     Mine = $false; Due = $null }
        @{ Type = "Task";  Title = "Crash reporting for the release channel";            Priority = "Medium";  Mine = $false; Due = 15 }
    )
    "NOVA" = @(
        @{ Type = "Epic";  Title = "Accessibility audit remediation";                    Priority = "High";    Mine = $true;  Due = 16 }
        @{ Type = "Story"; Title = "Keyboard navigation for the combobox";               Priority = "High";    Mine = $true;  Due = 1 }
        @{ Type = "Bug";   Title = "Focus ring invisible on the dark surface token";     Priority = "Medium";  Mine = $true;  Due = 4 }
        @{ Type = "Task";  Title = "Document the spacing scale";                         Priority = "Low";     Mine = $false; Due = 10 }
        @{ Type = "Story"; Title = "Toast component with a live region";                 Priority = "Medium";  Mine = $false; Due = 13 }
        @{ Type = "Task";  Title = "Publish the package to the internal registry";       Priority = "Medium";  Mine = $true;  Due = 6 }
        @{ Type = "Bug";   Title = "Date picker traps focus when opened twice";          Priority = "High";    Mine = $false; Due = 2 }
        @{ Type = "Story"; Title = "Motion tokens for reduced-motion preferences";       Priority = "Low";     Mine = $false; Due = $null }
    )
}

$created = @{}
$totalIssues = 0

foreach ($project in $projects) {
    $created[$project.Key] = @()
    $others = @($project.MemberIds)
    $index = 0

    foreach ($item in $backlog[$project.Key]) {
        $issue = Invoke-Api POST "/api/projects/$($project.Id)/issues" `
            @{ type = $item.Type; title = $item.Title } -Token $demo.Token

        # Assignee, priority, due date and estimate go on in one PATCH.
        $assignee = if ($item.Mine) { $demo.Id } else { $others[$index % $others.Count] }
        $patch = @{
            priority        = $item.Priority
            assigneeUserId  = $assignee
            description     = "Raised during sprint planning. See the linked design doc for acceptance criteria and the rollout checklist."
            estimate        = @(1, 2, 3, 5, 8)[$index % 5]
        }
        if ($null -ne $item.Due) {
            $patch.dueDateUtc = $today.AddDays($item.Due).ToString("yyyy-MM-dd")
        }
        Invoke-Api PATCH "/api/issues/$($issue.id)" $patch -Token $demo.Token | Out-Null

        # A couple of labels each.
        $pool = $labels[$project.Key]
        Invoke-Api POST "/api/issues/$($issue.id)/labels" `
            @{ labelId = $pool[$index % $pool.Count] } -Token $demo.Token | Out-Null

        $created[$project.Key] += [pscustomobject]@{ Id = $issue.id; Mine = $item.Mine; Type = $item.Type }
        $index++
        $totalIssues++
    }
    Write-Step "$($project.Key): $($backlog[$project.Key].Count) issues"
}

# ---------- board spread ----------

Write-Host "Board state" -ForegroundColor White

foreach ($project in $projects) {
    $issues = $created[$project.Key]
    $inProgress = 0
    $done = 0
    $i = 0

    foreach ($issue in $issues) {
        # Two in five in progress, one in five done - enough that all three
        # columns are populated and My Tasks has something filtered out. The
        # slots are picked out of one cycle so the two rules cannot collide and
        # starve the Done column.
        $slot = $i % 5
        if ($slot -eq 1 -or $slot -eq 3) {
            Move-Issue -User $demo -IssueId $issue.Id -ColumnId $project.Board.InProgress
            $inProgress++
        } elseif ($slot -eq 2 -and $issue.Type -ne "Epic") {
            # An Epic stays open while its children are still in flight.
            Move-Issue -User $demo -IssueId $issue.Id -ColumnId $project.Board.Done
            $done++
        }
        $i++
    }
    Write-Step "$($project.Key): $inProgress in progress, $done done"
}

# ---------- sprint contents ----------

foreach ($project in $projects) {
    $sprintId = $sprints[$project.Key].Current
    $take = $created[$project.Key] | Select-Object -First 6
    foreach ($issue in $take) {
        try {
            Invoke-Api POST "/api/sprints/$sprintId/issues" @{ issueId = $issue.Id } -Token $demo.Token | Out-Null
        } catch { }
    }
}
Write-Step "Active sprints populated"

# ---------- conversation ----------

Write-Host "Comments" -ForegroundColor White

$remarks = @(
    "Picked this up - the root cause looks like the retry policy, not the gateway."
    "Reproduced on staging. Adding the failing case to the test suite first."
    "Design signed off. Ready for review whenever someone has a slot."
    "Blocked on the API change landing. Moving to next sprint if it slips past Thursday."
    "Shipped behind a flag. Watching error rates before rolling it out fully."
    "Nice catch - this would have bitten us in the release."
    "Split the remaining work into a follow-up so this one can close."
)

$commentCount = 0
$speaker = 0
foreach ($project in $projects) {
    # Only project members can comment, so pick from the people actually on it.
    $voices = @($demo) + ($team | Where-Object { $project.MemberIds -contains $_.Id })

    foreach ($issue in ($created[$project.Key] | Select-Object -First 8)) {
        $rounds = 1 + ($speaker % 2)
        for ($r = 0; $r -lt $rounds; $r++) {
            $voice = $voices[($speaker + $r) % $voices.Count]
            try {
                Invoke-Api POST "/api/issues/$($issue.Id)/comments" `
                    @{ body = $remarks[($speaker + $r) % $remarks.Count] } -Token $voice.Token | Out-Null
                $commentCount++
            } catch { }
        }
        $speaker++
    }
}
Write-Step "$commentCount comments across the three projects"

# ---------- report ----------

$myTasks = Invoke-Api GET "/api/dashboard/my-tasks?limit=100" -Token $demo.Token
$myProjects = Invoke-Api GET "/api/dashboard/my-projects" -Token $demo.Token
$activity = Invoke-Api GET "/api/dashboard/recent-activity?limit=100" -Token $demo.Token

Write-Host "`nDone." -ForegroundColor Green
Write-Host "  Log in at http://localhost:3000/login" -ForegroundColor White
Write-Host "  $DemoEmail / $DemoPassword" -ForegroundColor White
Write-Host ""
Write-Host "  My Tasks       $($myTasks.items.Count) open, assigned to you" -ForegroundColor Gray
Write-Host "  My Projects    $($myProjects.items.Count)" -ForegroundColor Gray
Write-Host "  Activity       $($activity.items.Count) recent entries" -ForegroundColor Gray
Write-Host "  Issues         $totalIssues total across the workspace" -ForegroundColor Gray
Write-Host ""
