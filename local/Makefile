.DEFAULT_GOAL := help
CLUSTER_NAME  := todo-gitops
ARGOCD_NS     := argocd
MONITORING_NS := monitoring

# NOTE: Kind uses the 'standard' StorageClass (rancher.io/local-path provisioner).
# Prometheus PVC data survives pod restarts but is lost on `make teardown`.
# Host path /tmp/todo-gitops-prometheus is mounted via extraMounts in kind-config.yaml.

.PHONY: help cluster-up cluster-down install-nginx install-argocd bootstrap-argocd \
        apply-monitoring port-forward-argocd port-forward-grafana port-forward-prometheus \
        teardown status

help:
	@echo ""
	@echo "  make cluster-up           Create kind cluster + install nginx-ingress"
	@echo "  make install-argocd       Install ArgoCD via Kustomize (pinned v2.13.0)"
	@echo "  make bootstrap-argocd     Apply App of Apps to ArgoCD (apps + monitoring)"
	@echo "  make apply-monitoring     Apply Prometheus + Grafana manually if needed"
	@echo "  make port-forward-argocd  ArgoCD UI → https://localhost:8443"
	@echo "  make port-forward-grafana Grafana UI → http://localhost:3000"
	@echo "  make port-forward-prometheus Prometheus → http://localhost:9090"
	@echo "  make status               Show ArgoCD app sync status"
	@echo "  make teardown             Delete kind cluster"
	@echo ""

## ── Cluster ─────────────────────────────────────────────────────────────────

cluster-up:
	@echo "→ Creating kind cluster '$(CLUSTER_NAME)'..."
	@mkdir -p /tmp/todo-gitops-prometheus
	kind create cluster --config local/kind-config.yaml --name $(CLUSTER_NAME)
	@echo "→ Installing ingress-nginx..."
	$(MAKE) install-nginx
	@echo "✓ Cluster ready. Run: make install-argocd"

install-nginx:
	kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.10.1/deploy/static/provider/kind/deploy.yaml
	kubectl wait --namespace ingress-nginx \
	  --for=condition=ready pod \
	  --selector=app.kubernetes.io/component=controller \
	  --timeout=90s

cluster-down teardown:
	@echo "→ Deleting kind cluster '$(CLUSTER_NAME)'..."
	kind delete cluster --name $(CLUSTER_NAME)
	@echo "✓ Cluster deleted."

## ── ArgoCD ───────────────────────────────────────────────────────────────────

install-argocd:
	@echo "→ Creating argocd namespace..."
	kubectl create namespace $(ARGOCD_NS) --dry-run=client -o yaml | kubectl apply -f -
	@echo "→ Installing ArgoCD v2.13.0 via Kustomize remote base..."
	kubectl apply -k argocd/install/
	@echo "→ Waiting for ArgoCD server to be ready (this takes ~2 min)..."
	kubectl wait --namespace $(ARGOCD_NS) \
	  --for=condition=available deployment/argocd-server \
	  --timeout=180s
	@echo "✓ ArgoCD installed."
	@echo ""
	@echo "  Initial admin password:"
	@kubectl -n $(ARGOCD_NS) get secret argocd-initial-admin-secret \
	  -o jsonpath="{.data.password}" | base64 -d
	@echo ""
	@echo "  Run: make port-forward-argocd   → https://localhost:8443"
	@echo "  Then: make bootstrap-argocd"

bootstrap-argocd:
	@echo "→ Applying App of Apps..."
	kubectl apply -f argocd/apps/app-of-apps.yaml -n $(ARGOCD_NS)
	@echo "✓ App of Apps applied. ArgoCD will sync todo-api-dev, todo-api-prod, and monitoring from Git."

## ── Monitoring ───────────────────────────────────────────────────────────────

apply-monitoring:
	@echo "→ Applying Prometheus stack..."
	kubectl apply -f monitoring/prometheus/namespace.yaml
	kubectl apply -f monitoring/prometheus/rbac.yaml
	kubectl apply -f monitoring/prometheus/configmap.yaml
	kubectl apply -f monitoring/prometheus/pvc.yaml
	kubectl apply -f monitoring/prometheus/deployment.yaml
	kubectl apply -f monitoring/prometheus/service.yaml
	@echo "→ Applying Grafana stack..."
	kubectl apply -f monitoring/grafana/configmap-datasource.yaml
	kubectl apply -f monitoring/grafana/configmap-dashboard-provider.yaml
	kubectl apply -f monitoring/grafana/configmap-dashboard.yaml
	kubectl apply -f monitoring/grafana/deployment.yaml
	kubectl apply -f monitoring/grafana/service.yaml
	@echo "✓ Monitoring stack applied."
	@echo "  Run: make port-forward-grafana   → http://localhost:3000 (admin/admin)"

## ── Port Forwards ────────────────────────────────────────────────────────────

port-forward-argocd:
	@echo "→ ArgoCD UI at https://localhost:8443 (username: admin)"
	kubectl port-forward svc/argocd-server -n $(ARGOCD_NS) 8443:443

port-forward-grafana:
	@echo "→ Grafana at http://localhost:3000 (admin/admin)"
	kubectl port-forward svc/grafana -n $(MONITORING_NS) 3000:3000

port-forward-prometheus:
	@echo "→ Prometheus at http://localhost:9090"
	kubectl port-forward svc/prometheus -n $(MONITORING_NS) 9090:9090

## ── Status ───────────────────────────────────────────────────────────────────

status:
	@echo "=== ArgoCD Apps ==="
	kubectl get applications -n $(ARGOCD_NS)
	@echo ""
	@echo "=== todo-dev pods ==="
	kubectl get pods -n todo-dev
	@echo ""
	@echo "=== todo-prod pods ==="
	kubectl get pods -n todo-prod
	@echo ""
	@echo "=== monitoring pods ==="
	kubectl get pods -n $(MONITORING_NS)
