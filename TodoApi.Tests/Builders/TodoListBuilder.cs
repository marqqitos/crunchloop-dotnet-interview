namespace TodoApi.Tests.Builders;

public class TodoListBuilder
{
    private TodoList _todoList = new TodoList { Name = "Test List" };

    public TodoListBuilder WithId(int id)
    {
        _todoList.Id = id;
        return this;
    }

    public TodoListBuilder WithName(string name)
    {
        _todoList.Name = name;
        return this;
    }

    public TodoListBuilder WithExternalId(string externalId)
    {
        _todoList.ExternalId = externalId;
        return this;
    }

    public TodoListBuilder WithLastModified(DateTime lastModified)
    {
        _todoList.LastModified = lastModified;
        return this;
    }

    public TodoListBuilder WithLastSyncedAt(DateTime lastSyncedAt)
    {
        _todoList.LastSyncedAt = lastSyncedAt;
        return this;
    }

    public TodoListBuilder WithItem(TodoItem item)
    {
        _todoList.Items.Add(item);
        return this;
    }

    public TodoList Build() => _todoList;

    public static TodoListBuilder Create() => new TodoListBuilder();
}
