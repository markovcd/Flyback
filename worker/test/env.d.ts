import type { D1Migration } from "cloudflare:test";
import type { Env as WorkerEnv } from "../src/env";

declare global {
  namespace Cloudflare {
    interface Env extends WorkerEnv {
      MIGRATIONS: D1Migration[];
    }
  }
}
