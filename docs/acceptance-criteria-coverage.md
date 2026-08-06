# Acceptance Criteria Coverage

Task **T048** ([spec/22-tasks.md](../spec/22-tasks.md) §9): every §15 acceptance-criteria bullet in
feature documents [01](../spec/01-authentication.md)–[17](../spec/17-admin.md), mapped to the
integration test that verifies it.

Test names are abbreviated; the class column gives the file. All live in
`tests/JiraLite.Api.IntegrationTests`.

The point of this document is to be checkable, not reassuring: where a bullet is only partly
covered, or covered by something narrower than it sounds, that is stated in the row.

---

## 01 — Authentication

| # | Criterion | Covered by |
|---|---|---|
| 1 | Register creates a User, issues no tokens | `Auth/AuthenticationTests.Registering_creates_the_user_without_issuing_any_token` |
| 2 | Login returns a token pair; RefreshToken persisted hashed | `AuthenticationTests.Logging_in_returns_a_token_pair_and_persists_the_refresh_token_hashed` |
| 3 | Refresh rotates; old token revoked with `ReplacedByTokenId` set | `AuthenticationTests.Refreshing_rotates_the_token_and_links_the_old_one_to_its_replacement` |
| 4 | Replaying a revoked token revokes the whole family | `AuthenticationTests.Reusing_a_revoked_refresh_token_revokes_every_active_token_for_that_user` |
| 5 | Logout revokes the presented token; later refresh fails | `AuthenticationTests.Logout_revokes_the_presented_token_and_a_later_refresh_with_it_fails` |
| 6 | Deactivated account fails login with the generic message | `AuthenticationTests.A_deactivated_account_fails_login_with_the_same_message_as_a_wrong_password` |
| 7 | Unregistered email gets an identical reset response and no token | `Auth/PasswordResetTests.An_unknown_address_is_answered_exactly_like_a_registered_one_and_mints_nothing` |
| 8 | A requested reset persists the token hashed and mails the raw value | `PasswordResetTests.A_requested_reset_persists_the_token_hashed_never_in_the_clear` |
| 9 | Redeeming a token swaps the password over | `PasswordResetTests.Completing_a_reset_makes_the_new_password_work_and_the_old_one_fail` |
| 10 | A reset token is single-use | `PasswordResetTests.A_token_cannot_be_redeemed_twice` |
| 11 | An expired reset token is rejected | `PasswordResetTests.An_expired_token_is_rejected` |
| 12 | Requesting a second link invalidates the first | `PasswordResetTests.Requesting_a_second_link_invalidates_the_first` |
| 13 | Completing a reset revokes sessions opened beforehand | `PasswordResetTests.Completing_a_reset_revokes_every_live_session` |
| 14 | Deactivated user gets the same answer but no token | `PasswordResetTests.A_deactivated_account_gets_the_same_answer_but_no_token` |

## 02 — Users

| # | Criterion | Covered by |
|---|---|---|
| 1 | `UserProfile` + `NotificationPreference` exist with defaults after register alone | `Users/UserProfileTests.Registration_alone_creates_the_profile_and_notification_preferences_with_defaults` |
| 2 | Avatar upload updates `AvatarUrl`; previous file deleted | `UserProfileTests.Uploading_an_avatar_sets_the_url_and_deletes_the_file_it_replaced` |
| 3 | Delivery pipeline honours current preferences | `Notifications/NotificationDeliveryTests` (in-app off / both off) |
| 4 | Action writes an `ActivityLogEntry` visible at `/api/users/me/activity` | `Users/GetMyActivityTests.Creating_a_workspace_project_issue_and_comment_each_write_a_real_activity_entry` |
| 5 | `GET /api/users/{userId}` returns only id/displayName/avatarUrl | `UserProfileTests.Another_users_public_profile_exposes_only_display_name_and_avatar_never_the_email` |
| 6 | Deactivate: `IsActive` false, tokens revoked, login fails generically | `UserProfileTests.Deactivating_revokes_every_refresh_token_...` + `AuthenticationTests` (#6 above) |
| 7 | Deactivation modifies no membership or assignment (BR-09) | `UserProfileTests.Deactivation_leaves_memberships_and_assigned_issues_untouched` |

## 03 — Workspaces

| # | Criterion | Covered by |
|---|---|---|
| 1 | Workspace creator becomes `Admin` member | `Workspaces/WorkspaceMembershipTests.Creating_a_workspace_makes_the_creator_an_admin_member` |
| 2 | Invite creates a `Pending` invitation and dispatches an email | `Workspaces/CreateInvitationTests.Admin_invites_...` (row) + `NotificationDeliveryTests.An_invitation_emails_the_invitee_...` (email) |
| 3 | Accepting creates the member with the invited role; status `Accepted` | `WorkspaceMembershipTests.Accepting_an_invitation_creates_the_membership_with_the_invited_role_and_marks_it_accepted` |
| 4 | Sole Admin cannot be removed or demoted | `WorkspaceMembershipTests.The_sole_admin_can_be_neither_removed_nor_demoted` |
| 5 | Removing a member removes their `ProjectMember` rows | `Workspaces/RemoveMemberCascadeTests` |
| 6 | Non-Admin can leave unaided | `WorkspaceMembershipTests.A_non_admin_member_can_leave_on_their_own_and_loses_their_project_memberships` |
| 7 | Sole Admin leaving is rejected with 409 | `WorkspaceMembershipTests.The_sole_admin_cannot_leave_but_can_once_someone_else_is_promoted` |
| 8 | `GET /api/organizations` returns both owned Organizations | `WorkspaceMembershipTests.Listing_organizations_returns_every_organization_the_caller_owns` |

## 04 — Teams

| # | Criterion | Covered by |
|---|---|---|
| 1 | Admin creates a Team; it starts empty | `Teams/TeamTests.A_workspace_admin_creates_a_team_and_it_starts_with_no_members` |
| 2 | Team Lead adds a member without Admin involvement | `TeamTests.A_team_lead_adds_another_workspace_member_without_any_admin_involvement` |
| 3 | Adding a non-Workspace-member is rejected | `TeamTests.Adding_someone_who_is_not_a_workspace_member_is_rejected` |
| 4 | Deleting a Team removes `TeamMember` only | `TeamTests.Deleting_a_team_removes_its_memberships_and_nothing_else` |
| 5 | `IsLead` grants no Project/Issue access (BR-03) | `TeamTests.Being_a_team_lead_grants_no_project_access_of_its_own` |

## 05 — Projects

| # | Criterion | Covered by |
|---|---|---|
| 1 | Admin creates a Project with a unique key; creator is `ProjectAdmin` | `Projects/CreateProjectTests.Workspace_admin_creates_a_project_and_gets_a_default_board_with_three_columns` |
| 2 | Duplicate key in the same Workspace → 409 | `CreateProjectTests.Duplicate_key_within_the_same_workspace_is_rejected_case_insensitively` |
| 3 | Writes to an archived Project → 409 | `Comments/CommentTests.Adding_a_comment_on_an_archived_projects_issue_is_rejected`; `Boards/BoardTests.Creating_a_board_in_an_archived_project_is_rejected`; `Attachments/AttachmentTests.Uploading_to_an_archived_projects_issue_is_rejected_with_409` |
| 4 | Deleting a non-archived Project is rejected | `Projects/DeleteProjectTests.Deleting_a_non_archived_project_is_rejected` |
| 5 | Deleting an archived Project cascades everything below it | `DeleteProjectTests.Deleting_an_archived_project_cascades_and_detaches_activity_log_entries` |
| 6 | `ActivityLogEntry` rows survive with `ProjectId = NULL` | same test (the "detaches" half) |

## 06 — Boards

| # | Criterion | Covered by |
|---|---|---|
| 1 | New Project gets a default Kanban board with three columns | `Boards/BoardTests.Listing_boards_after_project_creation_returns_the_default_board` |
| 2 | Deleting a column holding Issues is rejected | `Boards/ColumnTests.Deleting_a_column_with_an_issue_placed_on_it_is_rejected` |
| 3 | Deleting the only Board is rejected | `Boards/DeleteBoardTests.Deleting_the_only_board_in_a_project_is_rejected` |
| 4 | A Board with a Completed Sprint is undeletable even with zero Issues (BR-09) | `DeleteBoardTests.A_board_with_a_completed_sprint_referencing_it_cannot_be_deleted_even_with_zero_issues` |
| 5 | Valid reorder updates every column's `DisplayOrder` atomically | `Boards/ReorderColumnsTests.Valid_reorder_updates_display_order_for_every_column` |
| 6 | Issues grouped by their current `BoardColumnId` | `Boards/GetBoardIssuesTests.Groups_issues_by_column_and_excludes_subtasks` |

## 07 — Backlog

| # | Criterion | Covered by |
|---|---|---|
| 1 | Product Backlog is `SprintId IS NULL`, Rank-ordered, no Subtasks | `Backlog/BacklogTests.Product_backlog_returns_issues_ordered_by_rank_excluding_subtasks` |
| 2 | Reposition changes only the moved Issue's Rank and sorts correctly | `Backlog/RepositionIssueRankTests.Repositioning_after_null_moves_the_issue_to_the_top` |
| 3 | Simultaneous reposition with a stale `rowVersion` → 409 | `Ranking/ConcurrentRankTests` (real races) + `RepositionIssueRankTests.Stale_row_version_is_rejected_with_409` |
| 4 | Exhausted Rank precision triggers a rebalance preserving order | `Ranking/RebalanceRanksJobTests.Rebalance_renumbers_a_squeezed_list_without_changing_relative_order`; `Ranking/LexoRankTests.Exceeding_max_rank_length_throws_precision_exhausted` |

## 08 — Sprints

| # | Criterion | Covered by |
|---|---|---|
| 1 | Starting a Planned Sprint sets `Active` + `StartedAtUtc` | `Sprints/StartSprintTests.Starting_a_planned_sprint_makes_it_active` |
| 2 | A second Active Sprint on the same Board is rejected | `StartSprintTests.Starting_a_second_sprint_while_one_is_already_active_on_the_same_board_is_rejected` |
| 3 | Complete without a carry-over target: Done keep `SprintId`, others null | `Sprints/SprintIssuesTests.Completing_a_sprint_carries_forward_incomplete_issues_and_keeps_done_ones` |
| 4 | Complete with `moveIncompleteIssuesToSprintId`: incomplete move there | same test |
| 5 | Deleting a Planned Sprint returns its Issues to the backlog | `SprintIssuesTests.Deleting_a_planned_sprint_returns_its_issues_to_the_product_backlog` |
| 6 | Completion records `CarriedForwardIssueCount` on the Sprint, not only in the response (BR-09) | `Sprints/SprintReportTests.A_completed_sprint_reports_what_was_carried_out_of_it` — asserted through the report rather than against the row directly |

## 09 — Issues

| # | Criterion | Covered by |
|---|---|---|
| 1 | Story without a column lands in the default column, bottom of backlog | `Issues/CreateIssueTests.Developer_creates_a_story_and_it_lands_in_the_default_column_with_medium_priority` |
| 2 | A Subtask's `SprintId` mirrors its parent's | `CreateIssueTests.Subtask_under_a_story_mirrors_the_stated_sprint_id_of_its_parent`; `Sprints/SprintIssuesTests.Adding_an_issue_assigns_it_and_cascades_to_its_subtasks` |
| 3 | Deleting an Epic leaves Stories with `ParentIssueId` cleared | `Issues/DeleteIssueTests.Deleting_an_epic_detaches_its_children_instead_of_deleting_them` |
| 4 | Deleting a Story deletes its Subtasks | `DeleteIssueTests.Deleting_a_story_cascades_its_subtasks` |
| 5 | Moving onto a Kanban column clears `SprintId` | `Issues/MoveIssueTests.Moving_onto_a_kanban_column_clears_sprint_id_and_cascades_to_subtasks` |
| 6 | Stale `rowVersion` on move → 409 | `MoveIssueTests.Stale_row_version_is_rejected_with_409`; `Ranking/ConcurrentRankTests.Concurrent_moves_...` |
| 7 | Blocking records the reason and starts the clock (BR-15) | `Issues/BlockIssueTests.Blocking_records_the_reason_and_starts_the_clock` (+ `Blocking_without_a_reason_is_rejected`) |
| 8 | Re-blocking rewrites the reason but keeps `BlockedSinceUtc` (BR-16) | `BlockIssueTests.Re_blocking_rewrites_the_reason_but_keeps_the_original_timestamp` |
| 9 | An Issue in a Done column cannot be blocked (BR-17) | `BlockIssueTests.Blocking_an_issue_in_a_done_column_is_rejected` |
| 10 | Unblocking clears all three fields; unblocking an unblocked Issue 409s (BR-18) | `BlockIssueTests.Unblocking_clears_the_flag_the_reason_and_the_timestamp`; `Unblocking_an_issue_that_is_not_blocked_is_rejected` |
| 11 | Moving a blocked Issue into a Done column clears its blocked state (BR-17, reverse) | `BlockIssueTests.Finishing_a_blocked_issue_clears_its_blocked_state` |

## 10 — Comments

| # | Criterion | Covered by |
|---|---|---|
| 1 | Developer's Comment persists with `UpdatedAtUtc = NULL` and is listed | `Comments/CommentTests.Developer_adds_a_comment_and_it_appears_in_the_list_oldest_first` |
| 2 | Non-author, non-admin edit → 403 | `CommentTests.Non_author_cannot_edit_another_users_comment` |
| 3 | Project Admin can delete another user's Comment | `CommentTests.Project_admin_can_moderate_delete_another_users_comment` |
| 4 | Assignee, reporter and prior commenters are notified; author is not | `Notifications/NotificationDeliveryTests.A_comment_notifies_the_assignee_the_reporter_and_prior_commenters_but_never_its_author` |
| 5 | Comment on an archived Project's Issue → 409 | `CommentTests.Adding_a_comment_on_an_archived_projects_issue_is_rejected` |

## 11 — Attachments

| # | Criterion | Covered by |
|---|---|---|
| 1 | Allowed type under 25 MB creates a row and stores the file | `Attachments/AttachmentTests.Developer_uploads_a_file_and_it_appears_in_the_list` |
| 2 | PNG preview served inline with the right content type | `AttachmentTests.Preview_of_an_image_is_served_inline` |
| 3 | ZIP preview → 415, download still works | `AttachmentTests.Preview_of_a_non_previewable_content_type_is_rejected_with_415` + `..._can_be_downloaded_...` |
| 4 | Uploader deleting it removes both row and file | `AttachmentTests.Uploader_deletes_their_own_attachment_and_the_file_is_removed` |
| 5 | Upload to an archived Project's Issue → 409 | `AttachmentTests.Uploading_to_an_archived_projects_issue_is_rejected_with_409` |

## 12 — Labels

| # | Criterion | Covered by |
|---|---|---|
| 1 | Project Admin creates a Label with a valid hex colour | `Labels/LabelTests.Project_admin_creates_a_label_and_it_appears_in_the_list` |
| 2 | Duplicate name in the same Project → 409 | `LabelTests.Duplicate_label_name_is_rejected_case_insensitively`; `Persistence/WorkTrackingSchemaTests.Label_name_is_unique_per_project_case_insensitively` |
| 3 | Deleting a Label removes associations, not Issues | `LabelTests.Deleting_a_label_removes_its_associations_without_deleting_the_issue` |
| 4 | Developer can attach an existing Label unaided | `LabelTests.Developer_can_attach_an_existing_label_without_project_admin` |
| 5 | Cross-Project attach → 400 | `LabelTests.Attaching_a_label_from_a_different_project_is_rejected` |

## 13 — Notifications

| # | Criterion | Covered by |
|---|---|---|
| 1 | Assignment notifies the assignee, not the actor | `Issues/EditIssueTests.Assigning_an_issue_notifies_the_new_assignee_but_not_the_actor` |
| 2 | A Comment notifies assignee and reporter, not its author | `NotificationDeliveryTests.A_comment_notifies_...` |
| 3 | In-app off + email on → no row, email still dispatched | `NotificationDeliveryTests.A_recipient_with_in_app_off_but_email_on_gets_no_row_and_still_gets_the_email` (and the both-off case) |
| 4 | Marking read sets `IsRead` and `ReadAtUtc` | `Notifications/NotificationTests.Marking_a_notification_read_sets_is_read_and_read_at` |
| 5 | An invitation emails the invitee with no Notification row | `NotificationDeliveryTests.An_invitation_emails_the_invitee_without_creating_a_notification_row` |

## 14 — Dashboard

| # | Criterion | Covered by |
|---|---|---|
| 1 | My Tasks spans Projects, excluding Done and archived by default | `Dashboard/DashboardTests.My_tasks_spans_projects_and_excludes_done_and_archived_by_default` |
| 2 | Workspace-Admin-only access does not put Project X in My Projects | `DashboardTests.My_projects_omits_a_project_the_caller_only_reaches_via_workspace_admin` |
| 3 | Recent Activity drops a Workspace after the user is removed from it | `DashboardTests.Activity_from_a_workspace_disappears_once_the_caller_is_removed_from_it` |
| 4 | `includeDone=true` includes Done-column Issues | `DashboardTests.My_tasks_spans_projects_and_excludes_done_and_archived_by_default` (asserts both the default and the inclusive query) |
| 5 | Project-scoped entries hidden, `ProjectId = NULL` entries shown (BR-06) | `DashboardTests.Recent_activity_hides_project_scoped_entries_from_a_member_without_that_project` |
| 6 | My Stats totals, status buckets and full priority list (FR-04, BR-07) | `DashboardTests.My_stats_counts_the_callers_issues_by_status_priority_and_due_state` |
| 7 | `days` clamped, window dense and ending on today (BR-09) | `DashboardTests.My_stats_streak_covers_every_day_in_the_window_and_ends_today` |
| 8 | Figures are the caller's own, not the Project's (BR-07, BR-08) | `DashboardTests.My_stats_counts_only_what_the_caller_did_and_owns` |

## 15 — Calendar

| # | Criterion | Covered by |
|---|---|---|
| 1 | Only in-range due dates returned | `Calendar/CalendarTests.Due_dates_returns_only_issues_inside_the_requested_range` |
| 2 | No range supplied → current calendar month | `CalendarTests.Due_dates_defaults_to_the_current_calendar_month_when_no_range_is_supplied` |
| 3 | Sprint Timeline merges two Scrum Boards chronologically | `CalendarTests.Sprint_timeline_aggregates_two_scrum_boards_chronologically` |
| 4 | Archived Project still returns Calendar data | `CalendarTests.Both_calendar_views_still_work_for_an_archived_project` |

## 16 — RBAC

| # | Criterion | Covered by |
|---|---|---|
| 1 | A Viewer is rejected from every write action | `Rbac/RbacTests.A_viewer_is_rejected_from_every_write_action_on_the_project` (11 write surfaces in one sweep) |
| 2 | `ProjectAdmin` cannot delete the Project; Workspace `Admin` can | `RbacTests.A_project_admin_cannot_delete_the_project_but_a_workspace_admin_can` |
| 3 | Workspace `Admin` with no `ProjectMember` row acts as `ProjectAdmin` | `RbacTests.A_workspace_admin_with_no_project_membership_acts_as_a_project_admin` |
| 4 | No membership at all → `effectiveRole` is `null` | `Projects/GetProjectTests.My_role_endpoint_returns_null_effective_role_for_a_user_with_no_access`; `RbacTests.A_user_with_no_membership_at_all_gets_a_null_effective_workspace_role` |
| 5 | Team Lead without a qualifying `ProjectMember` role → 403 (BR-05) | `Teams/TeamTests.Being_a_team_lead_grants_no_project_access_of_its_own` |
| 6 | `ProjectAdmin` deletes a Planned Sprint; `Developer` gets 403 | `RbacTests.Deleting_a_planned_sprint_is_a_project_admin_action_not_a_developer_one` |
| 7 | Author demoted to `Viewer` cannot delete their own Comment (BR-06) | `RbacTests.A_comment_author_demoted_to_viewer_can_no_longer_delete_their_own_comment`; edit half in `CommentTests.Author_demoted_to_viewer_can_no_longer_edit_their_own_comment` |
| 8 | A Workspace Member not on a Team can still view it | `TeamTests.A_workspace_member_who_is_not_on_the_team_can_still_view_it` |

## 17 — Admin

| # | Criterion | Covered by |
|---|---|---|
| 1 | Overview counts match exactly, archived Project included | `Admin/AdminEndpointsTests.Overview_counts_match_including_archived_projects` |
| 2 | `projectRoles` lists only real memberships | `AdminEndpointsTests.Users_list_only_shows_the_projects_a_member_actually_belongs_to` |
| 3 | Non-Admin gets 403 on every endpoint in the document | `AdminEndpointsTests.Nonadmin_is_rejected_with_403_on_every_admin_endpoint` |
| 4 | Roles catalog identical across Workspaces (BR-03) | `AdminEndpointsTests.Role_catalog_is_identical_across_two_different_workspaces` |

## 23 — MCP Server

Added in Phase 8, after T048's 01–17 sweep. Listed here so the coverage picture stays complete.

| # | Criterion | Covered by |
|---|---|---|
| 1 | Plaintext token in the 201 only, never in a later list | `Users/AccessTokenTests.The_plaintext_value_is_returned_once_at_creation_and_never_again` (+ `Only_the_hash_is_persisted`) |
| 2 | Advertised tool list matches §14; nothing excluded appears | `Mcp/McpReadToolTests.The_advertised_tool_list_matches_the_specification_exactly` + `No_destructive_or_administrative_tool_is_advertised` |
| 3 | `move_issue` as Developer: column changes, activity written, assignee+reporter notified | `Mcp/McpWriteToolTests.Move_issue_changes_the_column_logs_activity_and_notifies_the_assignee_and_reporter` |
| 4 | Every write tool refused for a Viewer, no state change | `McpWriteToolTests.Every_write_tool_is_refused_for_a_viewer_and_nothing_changes` |
| 5 | Demotion after token issuance takes effect (BR-01) | `McpWriteToolTests.A_user_demoted_after_the_token_was_issued_loses_write_access_with_that_same_token` |
| 6 | Revoked token rejected with 401, no tool runs | `Auth/PersonalAccessTokenAuthTests.A_revoked_token_stops_working_immediately` |
| 7 | PAT rejected by `/api/*` (BR-02) | `PersonalAccessTokenAuthTests.A_personal_access_token_is_rejected_by_the_rest_api` |
| 8 | JWT rejected by `/mcp` (BR-02) | `PersonalAccessTokenAuthTests.A_jwt_access_token_is_rejected_by_the_mcp_endpoint` |
| 9 | 11th active token 409s, existing 10 survive (BR-05) | `AccessTokenTests.An_eleventh_active_token_is_rejected_and_the_existing_ten_survive` |
| 10 | Deactivated owner's tokens stop authenticating (BR-07) | `PersonalAccessTokenAuthTests.A_deactivated_owners_tokens_stop_working_without_being_revoked` |
| 11 | `Mcp:Enabled=false` ⇒ `/mcp` 404s, rest of API unchanged | `Mcp/McpDisabledTests` (all three) |

## 24 — Reports

Added after T048's 01–17 sweep, alongside the Issue blocked state it reports on.

| # | Criterion | Covered by |
|---|---|---|
| 1 | Subtasks excluded from every figure (BR-02) | `Sprints/SprintReportTests.Subtasks_are_excluded_from_every_count` |
| 2 | Points summed with `unestimatedIssues` beside them; both percentages reported | `SprintReportTests.Points_are_summed_and_unestimated_issues_are_reported_beside_them` |
| 3 | Status buckets group by Column name, Done last, regardless of `DisplayOrder` (BR-09) | `SprintReportTests.Status_buckets_group_by_column_name_with_done_last` |
| 4 | Unassigned work gets its own `null`-user row (BR-10) | `SprintReportTests.Unassigned_work_gets_its_own_row_rather_than_being_dropped` |
| 5 | A Planned Sprint has null pace and null state (BR-04, BR-05) | `SprintReportTests.A_planned_sprint_has_no_pace_and_no_verdict` |
| 6 | Last day with nothing done ⇒ `OffTrack`/`WellBehindPace` alone, never doubled (BR-06) | `SprintReportTests.A_sprint_on_its_last_day_with_nothing_done_is_off_track` |
| 7 | 1-in-10 blocked ⇒ `AtRisk`/`BlockedWork`; 2-in-4 ⇒ `OffTrack`/`HeavilyBlocked` (BR-07) | `SprintReportTests.One_blocked_issue_among_many_puts_the_sprint_at_risk`; `Two_blockers_above_a_fifth_of_open_work_is_off_track` |
| 8 | A lone blocker stays `AtRisk` however small the Sprint — the count floor (BR-07) | `SprintReportTests.A_lone_blocker_stays_at_risk_however_small_the_sprint` |
| 9 | Overdue and due-after-end are both named, not just the first (BR-06) | `SprintReportTests.Overdue_work_and_work_due_after_the_sprint_are_both_reported` |
| 10 | An empty Sprint is 200 `OnTrack`/`EmptySprint`, not an error (BR-07) | `SprintReportTests.An_empty_sprint_is_on_track_rather_than_an_error` |
| 11 | A completed Sprint reads 100% **and** reports its carried-forward count (BR-08) | `SprintReportTests.A_completed_sprint_reports_what_was_carried_out_of_it` |
| 12 | Non-member gets 403; unknown Sprint gets 404 (§13, §14) | `SprintReportTests.A_non_member_cannot_read_the_report`; `An_unknown_sprint_is_a_404` |

Not covered by a test: the healthy path's reason list being empty is asserted
(`A_healthy_sprint_reports_on_track_with_nothing_to_say`), but the exact wording of each reason's
`detail` string is not — only its `code`. The codes are the contract; the prose is not.

---

## Regression run

The whole suite, against the current code:

```
dotnet test JiraLite.slnx
Passed!  -  Failed: 0,  Passed: 306,  Skipped: 0,  Total: 306,  Duration: 2 m 5 s
```

The Phase 7 baseline was 229; Phase 8 added 35 (33 MCP/token tests plus two `PersonalAccessToken`
index rows in `IndexCoverageTests`), reaching the 264 this document was written against. Of the 42
added since, 26 belong to the Sprint Report and the Issue blocked state it reports on — 11 in
`Issues/BlockIssueTests`, 15 in `Sprints/SprintReportTests`. The other 16 came from the work that
landed between (dashboard stats and charts among them) and are not itemised here.

Integration tests only — there is no separate unit-test project. Every test runs against a real
SQL Server in a Testcontainers container, migrated by the same EF migrations the production image
applies, so a schema regression fails the suite rather than being mocked away.

## What this pass added

Building this matrix is what surfaced the gaps. Four documents were materially under-covered when
Phase 7 began:

- **01 Authentication** — zero dedicated tests. Register and login were exercised only as setup
  inside `TestDataHelper`; rotation, reuse detection, logout and the deactivated-login path were
  not verified at all.
- **02 Users** — only the activity log was covered. Profile defaults, avatar replacement, the
  public-profile projection, and the deactivation guarantees were not.
- **03 Workspaces** — invitation *creation* was covered; acceptance, the last-Admin guard, leaving,
  and the organization listing were not.
- **04 Teams** — shipped in Phase 2 with no integration coverage whatsoever.

Plus the delivery half of Notifications (which channel fires, and for whom) and the cross-cutting
RBAC bullets that no single feature test owns.

## Deliberate limits

Every bullet in 01–17 has a test. These are the places where the test is narrower than the words
of the criterion might suggest:

- **Email delivery** is asserted by reading the job Hangfire enqueued, not by observing SMTP. The
  test host's `NoOpEmailSender` is deliberately stateless (one shared fixture serves every test
  class), so the enqueued job is the furthest downstream observable point.
- **Query plans** (`Persistence/QueryPlanTests`) assert against SQL Server's *estimated* plan at a
  seeded volume of 10,000 Issues. That is enough to catch an index the optimizer will not use; it
  is not a load test, and [spec/22-tasks.md](../spec/22-tasks.md) T046's "load-test" wording is met
  in the narrower sense of verified index usage plus verified concurrency behaviour.

## Related documents

- [docs/plans/2026-08-01-phase-7-hardening.md](plans/2026-08-01-phase-7-hardening.md) — the Phase 7 plan
- [docs/deployment-runbook.md](deployment-runbook.md) — T049
