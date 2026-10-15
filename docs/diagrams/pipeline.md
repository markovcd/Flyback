# The pipeline

What runs on GitHub Actions, by the name each workflow has in the Actions tab and the
file it is in. The rules it keeps are in [pipeline.md](../agents/rules/pipeline.md);
why it runs where it does is ADR-0195. `PipelineDiagramTests` fails when a workflow is
added, renamed or removed and this page does not say so.

```mermaid
flowchart TB
    push(["Every push to main, every pull request"]) --> build
    hand(["By hand: Run workflow"]) --> cloud

    subgraph build["Build · ci.yml"]
        b_pick["runner / pick<br/>Runner · runner.yml"] --> b_gate["build<br/>Docker gate on ubuntu-latest"]
        b_win["native / test (windows)<br/>Native tests · native-tests.yml"]
        b_mac["native / test (macos)<br/>only when the macos box is ticked"]
    end

    subgraph cloud["Cloud gate · cloud-gate.yml"]
        c_linux["linux<br/>Docker gate"]
        c_win["native / test (windows)"]
        c_mac["native / test (macos)<br/>CGL when macos-gl is ticked"]
        c_android["android<br/>the APK on an emulator"]
    end

    build -- "green on main" --> apk["Android · android.yml<br/>builds the APK"]
    build -- "green on main" --> site["Worker · worker.yml<br/>deploys the site"]
    build -- "green on the commit" --> release
    c_android -- "green on the commit" --> release
    b_mac -- "green on the commit, here or in Cloud gate" --> release

    subgraph release["Release · release.yml"]
        r_pick["runner<br/>started by /release"] --> r_release["release<br/>release.sh"] --> r_publish["publish<br/>tags and publishes"] --> r_install["install"]
        r_android["android<br/>the signed arm64 APK"] --> r_publish
        r_publish --> r_store["store<br/>Store package · store.yml<br/>packs, installs and attaches the MSIX"]
    end

    subgraph scheduled["Scheduled"]
        coverage["Coverage · coverage.yml<br/>Mondays"]
        members["Test-only members · test-only-members.yml<br/>Tuesdays"]
        validate["Validate · validate.yml<br/>the preset site, every ten minutes"]
    end

    pages["Pages · pages.yml<br/>the redirect stub, when deploy/pages changes"]
    cert["Android certificate · android-cert.yml<br/>once, when it lands on main"]

    bot(["Dependabot's pull request"]) --> d_locks
    subgraph dependabot["Dependabot · dependabot-prs.yml"]
        d_locks["locks → push<br/>NuGet: rewrites the lock files, starts Build on the branch"]
        d_merge["merge<br/>squashes into main, starts Build on main"]
    end
    build -- "green on a Dependabot branch" --> d_merge
```

Build, Coverage and Release each have a `self-hosted` box under Run workflow. Ticked
on `main`, `runner.yml` puts the Docker gate on the Linux self-hosted runner if it is
online, else the Windows one, else GitHub's. Nothing a push or a pull request starts
runs on a self-hosted machine.

Build's `macos` box adds the macOS leg, which a push leaves out for its cost (ADR-0200).
