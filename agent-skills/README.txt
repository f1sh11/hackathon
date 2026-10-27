Quick Skill editing workspace

Edit itinerary.zh-Hant.md and itinerary.en.md. Keep the entire original text;
append revised rules at the end, explicitly explaining precedence when necessary.
Update version.json to a new version. No credentials belong in these files.

Validate locally:
  python3 scripts/planning_skills.py build

Apply from a workstation/CloudShell with operator access to the existing AWS pod:
  python3 scripts/planning_skills.py push
The command validates the workspace, preserves prior appendices, saves the old
bundle, and atomically activates the new one on the existing persistent disk.
No image build, API restart, new server, public write endpoint or Secret read.
The App connection code cannot update Skills. Operator pod-exec permission is
required; normally the deployer account is read-only.

Inspect effective version/content using authenticated GET /planning/skills.
GET /planning/skills?language=en&download=true downloads the active full Skill.
The test result includes skillVersion; /planning/agent/status includes skillSource.
A request keeps one Skill snapshot even if a newer bundle is activated mid-run.

Review any removed rules before an explicit rollback:
  python3 scripts/planning_skills.py rollback --confirm-rollback
The previous override is restored, or the embedded Skill when there was none.

This workspace initially contains the exact embedded rules without additions.
A build intentionally fails until both languages contain an appended update.
Keep both Markdown edits, version.json and the generated bundle in source control.
After restoring a prior bundle, update your local files to that version before
preparing another append-only update. Image upgrades changing baseVersion require
re-exporting/reviewing the workspace; incompatible active bundles fail closed.

CloudShell without a repository checkout:
1. Build locally. Upload scripts/planning_skills.py and agent-skills/bundle.json
   using CloudShell Actions > Upload file. The bundle contains Skill text only.
2. With temporary operator permission on the existing pod, run:
     python3 ~/planning_skills.py push --bundle ~/bundle.json
3. Verify /planning/skills and run a planning JSON test, then revoke temporary
   operator permissions. Future updates upload only the new bundle.json.
