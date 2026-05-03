using Prometheus;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// In-memory TODO store
var todos = new List<TodoItem>();
var nextId = 1;

// Prometheus custom metrics
var todosCreated = Metrics.CreateCounter("todos_created_total", "Total TODOs created");
var todosDeleted = Metrics.CreateCounter("todos_deleted_total", "Total TODOs deleted");
var todosCount = Metrics.CreateGauge("todos_current_count", "Current number of TODOs");

// HTTP Request metrics (SRE critical)
var httpRequestDuration = Metrics.CreateHistogram(
    "http_request_duration_seconds",
    "HTTP request latency in seconds",
    new HistogramConfiguration
    {
        Buckets = new[] { 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10 },
        LabelNames = new[] { "method", "path", "status" }
    }
);

var httpRequestsTotal = Metrics.CreateCounter(
    "http_requests_total",
    "Total HTTP requests",
    new CounterConfiguration
    {
        LabelNames = new[] { "method", "path", "status" }
    }
);

// API-specific metrics
var todosFailed = Metrics.CreateCounter(
    "todos_operation_failed_total",
    "Failed TODO operations",
    new CounterConfiguration
    {
        LabelNames = new[] { "operation" }
    }
);

var todosCompleted = Metrics.CreateCounter(
    "todos_completed_total",
    "Total TODOs marked complete"
);

const string HomePage = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Todo API</title>
    <style>
        :root {
            color-scheme: light;
            --bg: #f3efe5;
            --panel: #fffaf0;
            --ink: #17202a;
            --muted: #5f6c7b;
            --line: #d7c9aa;
            --accent: #c26d35;
            --accent-dark: #7f3f1d;
            --danger: #a33b2f;
            --done: #3a7d44;
        }

        * { box-sizing: border-box; }

        body {
            margin: 0;
            font-family: Georgia, "Times New Roman", serif;
            background:
                radial-gradient(circle at top left, #f9d9b4 0, transparent 28%),
                radial-gradient(circle at top right, #f1c4a6 0, transparent 24%),
                linear-gradient(180deg, #f7f0df 0%, var(--bg) 100%);
            color: var(--ink);
            min-height: 100vh;
        }

        main {
            max-width: 960px;
            margin: 0 auto;
            padding: 40px 20px 64px;
        }

        .hero {
            display: grid;
            gap: 12px;
            margin-bottom: 28px;
        }

        h1 {
            margin: 0;
            font-size: clamp(2.2rem, 6vw, 4.6rem);
            line-height: 0.95;
            letter-spacing: -0.04em;
        }

        .subtitle {
            max-width: 700px;
            color: var(--muted);
            font-size: 1.05rem;
            line-height: 1.6;
        }

        .grid {
            display: grid;
            grid-template-columns: 1.2fr 0.8fr;
            gap: 18px;
        }

        .panel {
            background: color-mix(in srgb, var(--panel) 92%, white);
            border: 1px solid var(--line);
            border-radius: 22px;
            padding: 22px;
            box-shadow: 0 14px 40px rgba(71, 52, 24, 0.08);
        }

        .panel h2 {
            margin: 0 0 14px;
            font-size: 1.15rem;
        }

        form {
            display: grid;
            grid-template-columns: 1fr auto;
            gap: 10px;
            margin-bottom: 14px;
        }

        input, button {
            font: inherit;
            border-radius: 14px;
            border: 1px solid var(--line);
            padding: 12px 14px;
        }

        input {
            background: white;
            color: var(--ink);
        }

        button {
            background: var(--accent);
            color: white;
            border-color: transparent;
            cursor: pointer;
            transition: transform 140ms ease, background 140ms ease;
        }

        button:hover { background: var(--accent-dark); }
        button:active { transform: translateY(1px); }
        button.secondary { background: #e9dcc3; color: var(--ink); border-color: var(--line); }
        button.danger { background: var(--danger); }

        ul {
            list-style: none;
            padding: 0;
            margin: 0;
            display: grid;
            gap: 10px;
        }

        li {
            display: grid;
            grid-template-columns: auto 1fr auto;
            gap: 12px;
            align-items: center;
            padding: 14px;
            border-radius: 16px;
            border: 1px solid var(--line);
            background: rgba(255, 255, 255, 0.72);
        }

        li.done .title {
            text-decoration: line-through;
            color: var(--done);
        }

        .title {
            font-size: 1rem;
            line-height: 1.4;
            word-break: break-word;
        }

        .actions {
            display: flex;
            gap: 8px;
        }

        .stats {
            display: grid;
            grid-template-columns: repeat(2, minmax(0, 1fr));
            gap: 10px;
            margin-bottom: 14px;
        }

        .stat {
            padding: 14px;
            border-radius: 16px;
            background: rgba(255, 255, 255, 0.8);
            border: 1px solid var(--line);
        }

        .stat strong {
            display: block;
            font-size: 1.8rem;
            margin-top: 4px;
        }

        .meta {
            color: var(--muted);
            font-size: 0.95rem;
            line-height: 1.6;
        }

        .status {
            min-height: 24px;
            color: var(--muted);
            font-size: 0.95rem;
        }

        .links {
            display: flex;
            flex-wrap: wrap;
            gap: 10px;
            margin-top: 16px;
        }

        .links a {
            color: var(--accent-dark);
            text-decoration: none;
            border-bottom: 1px solid rgba(127, 63, 29, 0.25);
        }

        @media (max-width: 760px) {
            .grid { grid-template-columns: 1fr; }
            form { grid-template-columns: 1fr; }
            li { grid-template-columns: 1fr; }
            .actions { justify-content: flex-start; }
        }
    </style>
</head>
<body>
    <main>
        <section class="hero">
            <p>Local Browser UI</p>
            <h1>Todo API</h1>
            <div class="subtitle">A minimal UI backed by the same API endpoints, so you can create, complete, and delete todos directly from the browser while Prometheus and Grafana keep scraping the live metrics.</div>
        </section>

        <section class="grid">
            <div class="panel">
                <h2>Todo List</h2>
                <form id="todo-form">
                    <input id="title" name="title" placeholder="Write a new todo" maxlength="120" required>
                    <button type="submit">Add Todo</button>
                </form>
                <div class="status" id="status"></div>
                <ul id="todo-list"></ul>
            </div>

            <aside class="panel">
                <h2>Snapshot</h2>
                <div class="stats">
                    <div class="stat">
                        <span>Total</span>
                        <strong id="total-count">0</strong>
                    </div>
                    <div class="stat">
                        <span>Completed</span>
                        <strong id="completed-count">0</strong>
                    </div>
                </div>
                <div class="meta">
                    This page talks to <code>/todos</code>, <code>/health</code>, and the existing REST endpoints. Use it to generate traffic, then check Grafana for request and latency charts.
                </div>
                <div class="links">
                    <a href="/todos" target="_blank" rel="noreferrer">View JSON</a>
                    <a href="/health" target="_blank" rel="noreferrer">Health</a>
                    <a href="/metrics" target="_blank" rel="noreferrer">Metrics</a>
                </div>
            </aside>
        </section>
    </main>

    <script>
        const list = document.getElementById('todo-list');
        const form = document.getElementById('todo-form');
        const titleInput = document.getElementById('title');
        const statusNode = document.getElementById('status');
        const totalCount = document.getElementById('total-count');
        const completedCount = document.getElementById('completed-count');

        function setStatus(message) {
            statusNode.textContent = message;
        }

        function escapeHtml(value) {
            return value
                .replaceAll('&', '&amp;')
                .replaceAll('<', '&lt;')
                .replaceAll('>', '&gt;')
                .replaceAll('"', '&quot;')
                .replaceAll("'", '&#39;');
        }

        async function request(url, options) {
            const response = await fetch(url, {
                headers: { 'Content-Type': 'application/json' },
                ...options
            });

            if (!response.ok && response.status !== 204) {
                const message = await response.text();
                throw new Error(message || 'Request failed');
            }

            return response.status === 204 ? null : response.json();
        }

        async function loadTodos() {
            const todos = await request('/todos');
            totalCount.textContent = String(todos.length);
            completedCount.textContent = String(todos.filter(todo => todo.isComplete).length);

            if (todos.length === 0) {
                list.innerHTML = '<li><div class="title">No todos yet. Add one to generate API traffic.</div></li>';
                return;
            }

            list.innerHTML = todos.map(todo => `
                <li class="${todo.isComplete ? 'done' : ''}">
                    <label>
                        <input type="checkbox" data-action="toggle" data-id="${todo.id}" ${todo.isComplete ? 'checked' : ''}>
                    </label>
                    <div class="title">${escapeHtml(todo.title)}</div>
                    <div class="actions">
                        <button class="secondary" data-action="toggle" data-id="${todo.id}">${todo.isComplete ? 'Reopen' : 'Complete'}</button>
                        <button class="danger" data-action="delete" data-id="${todo.id}">Delete</button>
                    </div>
                </li>`).join('');
        }

        form.addEventListener('submit', async (event) => {
            event.preventDefault();
            const title = titleInput.value.trim();
            if (!title) {
                setStatus('Title cannot be empty.');
                return;
            }

            await request('/todos', {
                method: 'POST',
                body: JSON.stringify({ title })
            });

            titleInput.value = '';
            setStatus('Todo created.');
            await loadTodos();
        });

        list.addEventListener('click', async (event) => {
            const target = event.target;
            if (!(target instanceof HTMLElement)) {
                return;
            }

            const button = target.closest('[data-action]');
            if (!button) {
                return;
            }

            const id = Number(button.getAttribute('data-id'));
            const action = button.getAttribute('data-action');
            const todos = await request('/todos');
            const todo = todos.find(item => item.id === id);
            if (!todo) {
                setStatus('Todo not found.');
                await loadTodos();
                return;
            }

            if (action === 'delete') {
                await request(`/todos/${id}`, { method: 'DELETE' });
                setStatus('Todo deleted.');
            }

            if (action === 'toggle') {
                await request(`/todos/${id}`, {
                    method: 'PUT',
                    body: JSON.stringify({ title: todo.title, isComplete: !todo.isComplete })
                });
                setStatus(todo.isComplete ? 'Todo reopened.' : 'Todo completed.');
            }

            await loadTodos();
        });

        list.addEventListener('change', async (event) => {
            const target = event.target;
            if (!(target instanceof HTMLInputElement) || target.dataset.action !== 'toggle') {
                return;
            }

            const id = Number(target.dataset.id);
            const todos = await request('/todos');
            const todo = todos.find(item => item.id === id);
            if (!todo) {
                setStatus('Todo not found.');
                await loadTodos();
                return;
            }

            await request(`/todos/${id}`, {
                method: 'PUT',
                body: JSON.stringify({ title: todo.title, isComplete: target.checked })
            });

            setStatus(target.checked ? 'Todo completed.' : 'Todo reopened.');
            await loadTodos();
        });

        loadTodos().catch(error => {
            setStatus(error.message);
        });
    </script>
</body>
</html>
""";

// Health endpoint — required for k8s liveness/readiness probes
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/", () => Results.Content(HomePage, "text/html"));

// Prometheus metrics endpoint
app.MapMetrics("/metrics");

app.MapGet("/todos", () =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    sw.Stop();
    httpRequestDuration.Labels("GET", "/todos", "200").Observe(sw.Elapsed.TotalSeconds);
    httpRequestsTotal.Labels("GET", "/todos", "200").Inc();
    return todos;
});

app.MapGet("/todos/{id}", (int id) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var todo = todos.FirstOrDefault(t => t.Id == id);
    sw.Stop();

    if (todo is null)
    {
        httpRequestsTotal.Labels("GET", "/todos/{id}", "404").Inc();
        return Results.NotFound();
    }

    httpRequestDuration.Labels("GET", "/todos/{id}", "200").Observe(sw.Elapsed.TotalSeconds);
    httpRequestsTotal.Labels("GET", "/todos/{id}", "200").Inc();
    return Results.Ok(todo);
});

app.MapPost("/todos", (CreateTodo input) =>
{
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            httpRequestsTotal.Labels("POST", "/todos", "400").Inc();
            todosFailed.Labels("create").Inc();
            return Results.BadRequest("Title cannot be empty");
        }

        var todo = new TodoItem(nextId++, input.Title, false);
        todos.Add(todo);

        sw.Stop();
        httpRequestDuration.Labels("POST", "/todos", "201").Observe(sw.Elapsed.TotalSeconds);
        httpRequestsTotal.Labels("POST", "/todos", "201").Inc();
        todosCreated.Inc();
        todosCount.Inc();

        return Results.Created($"/todos/{todo.Id}", todo);
    }
    catch (Exception)
    {
        httpRequestsTotal.Labels("POST", "/todos", "500").Inc();
        todosFailed.Labels("create").Inc();
        return Results.StatusCode(500);
    }
});

app.MapPut("/todos/{id}", (int id, UpdateTodo input) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var index = todos.FindIndex(t => t.Id == id);

    if (index == -1)
    {
        sw.Stop();
        httpRequestsTotal.Labels("PUT", "/todos/{id}", "404").Inc();
        todosFailed.Labels("update").Inc();
        return Results.NotFound();
    }

    var oldTodo = todos[index];
    var newTodo = oldTodo with { Title = input.Title, IsComplete = input.IsComplete };
    todos[index] = newTodo;

    if (!oldTodo.IsComplete && newTodo.IsComplete)
    {
        todosCompleted.Inc();
    }

    sw.Stop();
    httpRequestDuration.Labels("PUT", "/todos/{id}", "200").Observe(sw.Elapsed.TotalSeconds);
    httpRequestsTotal.Labels("PUT", "/todos/{id}", "200").Inc();

    return Results.Ok(todos[index]);
});

app.MapDelete("/todos/{id}", (int id) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var index = todos.FindIndex(t => t.Id == id);

    if (index == -1)
    {
        sw.Stop();
        httpRequestsTotal.Labels("DELETE", "/todos/{id}", "404").Inc();
        todosFailed.Labels("delete").Inc();
        return Results.NotFound();
    }

    todos.RemoveAt(index);

    sw.Stop();
    httpRequestDuration.Labels("DELETE", "/todos/{id}", "204").Observe(sw.Elapsed.TotalSeconds);
    httpRequestsTotal.Labels("DELETE", "/todos/{id}", "204").Inc();
    todosDeleted.Inc();
    todosCount.Dec();

    return Results.NoContent();
});

app.Run();

record TodoItem(int Id, string Title, bool IsComplete);
record CreateTodo(string Title);
record UpdateTodo(string Title, bool IsComplete);
