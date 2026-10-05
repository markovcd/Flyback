import type { Env } from "./env";

/**
 * Asks GitHub to run the Validate workflow now. Without a token, or if GitHub does not
 * answer, the workflow's own schedule picks the submission up a few minutes later.
 */
export async function dispatchValidate(env: Env): Promise<void> {
  if (!env.GITHUB_DISPATCH_TOKEN || !env.GITHUB_REPOSITORY || !env.VALIDATE_WORKFLOW) return;

  try {
    await fetch(
      `https://api.github.com/repos/${env.GITHUB_REPOSITORY}/actions/workflows/${env.VALIDATE_WORKFLOW}/dispatches`,
      {
        method: "POST",
        headers: {
          Authorization: `Bearer ${env.GITHUB_DISPATCH_TOKEN}`,
          Accept: "application/vnd.github+json",
          "User-Agent": "flyback-site",
          "X-GitHub-Api-Version": "2022-11-28",
        },
        body: JSON.stringify({ ref: "main" }),
      },
    );
  } catch {
    // The schedule is the net.
  }
}
