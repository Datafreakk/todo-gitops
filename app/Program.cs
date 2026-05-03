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

// Health endpoint — required for k8s liveness/readiness probes
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

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
