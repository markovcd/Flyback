import { applyD1Migrations, env } from "cloudflare:test";
import { beforeEach } from "vitest";

await applyD1Migrations(env.DB, env.MIGRATIONS);

const TABLES = ["preset_tags", "presets", "defaults", "plugins", "plugin_defaults", "reports", "ratings", "letters", "limits"];

/** Each test starts from an empty site: no rows and no files. */
beforeEach(async () => {
  await env.DB.batch(TABLES.map((table) => env.DB.prepare(`DELETE FROM ${table}`)));

  const listed = await env.FILES.list();
  if (listed.objects.length > 0) await env.FILES.delete(listed.objects.map((o) => o.key));
});
