/** The bindings, variables and secrets wrangler.jsonc and deploy/cloudflare/README.md give the Worker. */
export interface Env {
  DB: D1Database;
  FILES: R2Bucket;
  ASSETS: Fetcher;

  /** The Access team's address, such as https://example.cloudflareaccess.com. Admin mode is off without it. */
  ACCESS_TEAM_DOMAIN?: string;

  /** The Access application's audience tag. Admin mode is off without it. */
  ACCESS_AUD?: string;

  /** owner/name of the repository whose Validate workflow is started on each submission. */
  GITHUB_REPOSITORY?: string;
  VALIDATE_WORKFLOW?: string;

  /** A token that can start workflows in GITHUB_REPOSITORY; without one, Validate's schedule picks submissions up. */
  GITHUB_DISPATCH_TOKEN?: string;

  POSTS_PER_HOUR?: string;
  REPORTS_PER_HOUR?: string;
  LETTERS_PER_HOUR?: string;
  RATINGS_PER_HOUR?: string;
}
