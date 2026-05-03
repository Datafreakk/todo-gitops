# todo-gitops

A production-like GitOps platform running locally on [kind](https://kind.sigs.k8s.io/), built for learning SRE and platform engineering concepts.

## What this repo is

A mono-repo containing a .NET 9 minimal API (TodoApi) and the full GitOps platform to deploy and operate it:

- **App** — TodoApi with REST endpoints, Prometheus metrics, and health checks
- **CI** — GitHub Actions builds a multi-platform Docker image, scans with Trivy, pushes to GHCR
- **CD** — ArgoCD watches this repo and automatically deploys changes to the local kind cluster
- **GitOps promotion** — dev deploys automatically on every push; prod requires a PR approval
- **Observability** — Prometheus scrapes metrics, Grafana shows a pre-built dashboard

## Architecture

### CI/CD GitOps Flow

```mermaid
flowchart LR
    subgraph DEV["👨‍💻 Developer"]
        code["git push\nto main"]
    end

    subgraph GH["GitHub"]
        direction TB
        ci["GitHub Actions CI\n─────────────────\n• Build multi-platform image\n  (linux/amd64 + linux/arm64)\n• Push → GHCR\n• Commit updated image tag\n  to k8s/overlays/dev/"]
        repo[("Git Repo\ntodo-gitops")]
        ghcr[("GHCR\nghcr.io/datafreakk/\ntodo-api:sha-xxxx")]
        ci -->|push image| ghcr
        ci -->|commit tag| repo
    end

    subgraph CLUSTER["☸️ kind Cluster (local)"]
        direction TB
        argo["ArgoCD\n─────────────\npolls repo every 3 min\ndetects changed tag\nruns kustomize build\napplies diff"]
        subgraph NAMESPACES["Namespaces"]
            dev["todo-dev\n────────\nreplicas=1\nauto-sync"]
            prod["todo-prod\n──────────\nreplicas=3\nPR-gated"]
        end
        subgraph OBS["Observability"]
            prom["Prometheus\nscrapes /metrics"]
            graf["Grafana\ndashboard"]
            prom --> graf
        end
        argo -->|sync| dev
        argo -->|sync on PR merge| prod
        dev -->|/metrics| prom
        prod -->|/metrics| prom
    end

    code --> ci
    repo -->|watched by| argo
    ghcr -->|pulled by| CLUSTER
```

### Cluster Internal Design

```mermaid
flowchart TB
    subgraph KIND["kind Cluster"]
        direction TB

        subgraph ARGOCD["argocd namespace"]
            aoa["App of Apps\n(root ArgoCD app)"]
            app_dev["ArgoCD App\ntodo-api-dev"]
            app_prod["ArgoCD App\ntodo-api-prod"]
            app_mon["ArgoCD App\nmonitoring"]
            aoa --> app_dev
            aoa --> app_prod
            aoa --> app_mon
        end

        subgraph TODODEV["todo-dev namespace"]
            dep_dev["Deployment\nreplicas=1\ncpu: 50m–500m\nmem: 64Mi–256Mi"]
            svc_dev["Service\nClusterIP :80"]
            sa_dev["ServiceAccount\ntodo-api"]
            np_dev["NetworkPolicy\ndefault-deny ingress\nallow: prometheus, ingress-nginx"]
            dep_dev --- svc_dev
        end

        subgraph TODOPROD["todo-prod namespace"]
            dep_prod["Deployment\nreplicas=3\ncpu: 100m–1000m\nmem: 128Mi–512Mi"]
            svc_prod["Service\nClusterIP :80"]
        end

        subgraph MON["monitoring namespace"]
            prom["Prometheus\nClusterRole → scrape\nall namespaces\nPVC 2Gi"]
            graf["Grafana\npre-built dashboard\nport 3000"]
            prom --> graf
        end

        subgraph INGRESS["ingress-nginx namespace"]
            nginx["ingress-nginx\nports 80/443\nexposed to host"]
        end

        app_dev -->|kustomize build\noverlays/dev| dep_dev
        app_prod -->|kustomize build\noverlays/prod| dep_prod
        prom -->|scrape :8080/metrics| dep_dev
        prom -->|scrape :8080/metrics| dep_prod
        nginx -->|route| svc_dev
    end

    user(["User / curl"])
    user -->|port-forward| svc_dev
```

### Promotion Flow (dev → prod)

```mermaid
sequenceDiagram
    participant Dev as Developer
    participant GH as GitHub Actions
    participant GHCR as GHCR Registry
    participant Repo as Git Repo
    participant Argo as ArgoCD
    participant DevNS as todo-dev
    participant ProdNS as todo-prod

    Dev->>GH: git push main
    GH->>GHCR: docker push sha-abc1234
    GH->>Repo: commit: update dev tag → sha-abc1234
    Argo->>Repo: poll (every 3 min)
    Argo->>DevNS: kustomize apply (auto-sync)
    Note over DevNS: New pod running sha-abc1234

    Dev->>GH: workflow_dispatch: promote sha-abc1234
    GH->>Repo: open PR: update prod tag → sha-abc1234
    Dev->>Repo: review + merge PR
    Argo->>Repo: poll detects prod change
    Argo->>ProdNS: kustomize apply (auto-sync)
    Note over ProdNS: 3 replicas running sha-abc1234
```

## Repo structure

```
todo-gitops/
├── app/                        # TodoApi .NET 9 source + Dockerfile
├── k8s/
│   ├── base/                   # shared k8s manifests (deployment, service, networkpolicy)
│   └── overlays/
│       ├── dev/                # dev: replicas=1, lighter resource limits, auto-sync
│       └── prod/               # prod: replicas=3, stricter limits, PR-gated promotion
├── argocd/
│   ├── install/                # ArgoCD install via Kustomize remote base (pinned v2.13.0)
│   └── apps/                   # App of Apps pattern — root app manages all child apps
├── monitoring/
│   ├── prometheus/             # Prometheus with ClusterRole for cross-namespace scraping
│   └── grafana/                # Grafana with pre-built TodoApi dashboard
└── local/
    ├── kind-config.yaml        # 1 control-plane + 2 workers, ports 80/443
    └── Makefile                # bootstrap commands (cluster, argocd, monitoring, port-forwards)
```

## API endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/todos` | List all todos |
| GET | `/todos/{id}` | Get todo by id |
| POST | `/todos` | Create todo `{"title": "..."}` |
| PUT | `/todos/{id}` | Update todo `{"title": "...", "isComplete": true}` |
| DELETE | `/todos/{id}` | Delete todo |
| GET | `/health` | Health check (used by k8s probes) |
| GET | `/metrics` | Prometheus metrics |

## Local setup

### Prerequisites
- Docker Desktop running
- `kind` — `brew install kind`
- `kubectl` — `brew install kubectl`

### Bootstrap (first time only)

```bash
# 1. Create kind cluster (1 control-plane + 2 workers)
make -f local/Makefile cluster-up

# 2. Install ArgoCD v2.13.0
make -f local/Makefile install-argocd

# 3. Apply App of Apps — ArgoCD takes over from here, including monitoring
make -f local/Makefile bootstrap-argocd
```

### Access UIs

```bash
# ArgoCD UI — https://localhost:8443 (user: admin, password printed after install)
make -f local/Makefile port-forward-argocd

# Grafana — http://localhost:3000 (admin/admin)
make -f local/Makefile port-forward-grafana

# Prometheus — http://localhost:9090
make -f local/Makefile port-forward-prometheus

# TodoApi (dev)
kubectl port-forward svc/todo-api -n todo-dev 8080:80
curl http://localhost:8080/health
curl http://localhost:8080/todos
```

### Check status

```bash
make -f local/Makefile status
```

## GitOps flow

### Dev (automatic)
1. Push to `main` → GitHub Actions builds image → pushes to GHCR
2. CI commits updated image tag to `k8s/overlays/dev/kustomization.yaml`
3. ArgoCD detects the commit → syncs `todo-dev` namespace automatically

### Prod (PR-gated)
1. Go to **Actions → Promote to Prod → Run workflow** and enter the image tag
2. Workflow opens a PR updating `k8s/overlays/prod/kustomization.yaml`
3. Review + merge PR → ArgoCD syncs `todo-prod` namespace

## Key design decisions

| Decision | Reason |
|----------|--------|
| Mono-repo | Simpler for learning; in production split app and infra repos for independent access control |
| Kustomize overlays (not Helm) | Plain YAML diffs in Git, no template syntax to debug |
| ArgoCD Kustomize remote base | Upgrades = bump tag in one line + PR; no vendored files |
| `targetRevision: main` + branch protection | Direct pushes blocked; every change goes through PR + CI |
| Multi-platform image (amd64+arm64) | amd64 for cloud/CI runners, arm64 for Apple Silicon local kind |
| GHCR (public) | Free, no credentials needed in cluster for public images |
| ClusterRole for Prometheus | Enables cross-namespace scraping (todo-dev + todo-prod → monitoring) |
| NetworkPolicy default-deny | Least-privilege networking; only prometheus and ingress-nginx allowed to reach app pods |
| Immutable image tags (SHA) | `sha-abc1234` not `latest`; every deployment is traceable to a Git commit |

## Teardown

```bash
make -f local/Makefile teardown
```
