SydneyBuddy AWS co-location trial — 2026-10-27, Australia/Sydney

Current app address:
https://api.a6c43f1d-2765-487d-89af-99b612b39de9.cloud.zetaris.com/sydneybuddy

The backend, PostgreSQL and authenticated gateway run in one pod on the existing
Zetaris driver node. The existing two-node Zetaris cluster is retained. There is
no new EC2 instance, node group, LoadBalancer Service or load balancer. A single
more-specific path was added to the existing zetaris-ndp-api ingress; its prior
routes, hostname, TLS certificate and load balancer remain in place.

Deployment record: deployment-record.json
The app access code is kept in the user's private runtime settings and the App's
Keychain/Keystore. NVIDIA, TfNSW and Zetaris credentials stay in Kubernetes Secrets;
they must never be placed in the App or Git. The pod has no service-account token.
The local/cloud backend selector continues to work; changing the cloud URL does
not require the phone to configure Zetaris separately.

Resource requests total 260m CPU and 1312 MiB memory. This is a low-load trial,
not a performance or availability guarantee. The hostname pin prevents this
workload from being scheduled on newly added hosts. If the managed node is
replaced, it will remain pending until the replacement is explicitly reviewed;
do not remove the pin or increase node-group capacity automatically.

New encrypted gp2 volumes: PostgreSQL 5 GiB, app state 2 GiB. PVCs are retained
when the StatefulSet is removed or scaled down. Stopping the pod does not stop
storage charges. At the verified rates, 7 GiB of gp2 plus the current private
image is approximately USD 0.86/month before taxes and existing compute/traffic.
This is an estimate, not an enforced billing cap.

Oracle remains available as a rollback/test source. AWS contains the verified
snapshot copied during migration. Subsequent writes on Oracle and AWS are not
automatically synchronized. Do not switch between them expecting shared updates.

Reproduction:
- render_aws_colocation.py accepts only pinned image digests, one existing node,
  a ClusterIP service and the approved encrypted storage budget.
- deploy_aws_colocation.py is copied into the private CloudShell staging directory
  beside the rendered release and approved private backup. It checks the exact
  cluster context, capacity and clean resource names before any initial restore.
- Run restore, then start, then publish as separate reviewed phases. Restore is
  blocked after completion and must not overwrite an existing trial database.
- Restore validates table counts against a successful isolated local rehearsal.
- Keep ingress.before.json and nodes.before.json from CloudShell for recovery.
- Rollback: with approved temporary deployment permissions, remove only the
  /sydneybuddy/ path that points to Service sydneybuddy:8080; preserve any other
  changes to the managed ingress. Switch the App back to the saved Oracle URL.
  Scale only StatefulSet zetaris/sydneybuddy to zero if requested; retain its
  PVCs until data is backed up and deletion is explicitly requested.
- Temporary deployment admin policy was removed after validation. Only the
  existing view policy and node-capacity reader remain. No Access Keys were made.

Skill update — 2026-10-27.5
The existing StatefulSet API image was updated in place; the app Pod was recreated
on the same driver node. Existing gateway, database/state volumes, Secret refs,
resources and ingress were preserved. update_aws_colocation.py saves the prior
workload and automatically restores the prior image on failed readiness/health.
The public endpoint reports Skill 2026-10-27.5. QVB source aliases were consolidated
in a live cloud-data search. A real NVIDIA planning draft passed without saving
a trip. The namespace-scoped AmazonEKSEditPolicy was removed after the update;
AmazonEKSViewPolicy remains. No credentials were read or changed for this release.
The new Skill includes transit-first/full-day/rest-separated walking preferences;
existing deterministic step caps and live rest counters remain unchanged. Raw
Zetaris source tables were not rewritten; the backend applies query-time identity
merging and trip-wide physical-place duplicate checks.

JSON test API and Skill hot reload update — 2026-10-27
POST /planning/test accepts a raw JSON object or one multipart file field;
?download=true returns an attachment. GET /planning/test/example downloads a
request example. Tests never save trips, but normal planning run diagnostics apply.
The new planning route loads the managed Zetaris snapshot just like /plan.
Live tests verified a one-day culture plan and a two-day nature plan with distinct
budgets, step caps and interests; both downloads and uploads passed.
GET /planning/skills returns effective bilingual content/version/SHA256.
Operator-only appendices activate atomically on the existing app-state PVC and
are captured per HTTP request. A neutral-marker live probe verified activation
and rollback without restart, restoring embedded Skill 2026-10-27.5 afterward.
The shared App token has no Skill write endpoint. Temporary namespace edit
permissions are revoked after deployment. See docs/PLANNING-JSON-TEST-API.md and
docs/PLANNING-SKILL-WORKFLOW.md. No new hosts or storage resources were added.

Skill administration API deployment — 2026-10-27
The existing API container was updated to the pinned skill-admin image. A new
independent admin credential is stored in a mode-0600 private file on the existing
app-state PVC and referenced by SKILL_ADMIN_TOKEN_FILE. The App bearer alone
cannot manage Skills. No new node, load balancer, database or disk was created.
Live HTTPS checks verified raw JSON and full bilingual Markdown candidate uploads,
preview, missing/wrong admin rejection, missing App bearer rejection, stale
activation rejection, and unchanged embedded Skill content/version 2026-10-27.5.
The deployment-check candidate was not activated. Postman status returned 200.
Temporary AmazonEKSEditPolicy was removed; AmazonEKSViewPolicy remains.
Use deploy_aws_skill_admin.py only in an explicitly authorized update session.
It preserves the existing container settings, adds the admin file reference, and
restores the previous API container configuration if deployment validation fails.
