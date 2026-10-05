import { cloudflareTest, readD1Migrations } from "@cloudflare/vitest-pool-workers";
import { defineConfig } from "vitest/config";

// The Worker runs in workerd against a local D1 and R2, with the migrations applied
// before each file and Access pointed at a team only the tests sign for.
export default defineConfig(async () => ({
  plugins: [
    cloudflareTest({
      wrangler: { configPath: "./wrangler.jsonc" },
      miniflare: {
        assets: { directory: "./test/assets", binding: "ASSETS" },
        bindings: {
          MIGRATIONS: await readD1Migrations("./migrations"),
          ACCESS_TEAM_DOMAIN: "https://flyback-test.cloudflareaccess.com",
          ACCESS_AUD: "flyback-test-audience",
        },
      },
    }),
  ],
  test: {
    setupFiles: ["./test/migrate.ts"],
  },
}));
