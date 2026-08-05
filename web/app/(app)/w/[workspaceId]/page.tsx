import { redirect } from "next/navigation";

/**
 * A workspace has no landing screen of its own — the switcher in the rail
 * points here, and projects are what you came for.
 */
export default async function WorkspaceHome({
  params,
}: {
  params: Promise<{ workspaceId: string }>;
}) {
  const { workspaceId } = await params;
  redirect(`/w/${workspaceId}/projects`);
}
