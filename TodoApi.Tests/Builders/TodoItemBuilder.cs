using TodoApi.Models;

namespace TodoApi.Tests.Builders;

public class TodoItemBuilder
{
    private TodoItem _todoItem = new TodoItem { Description = "Description" };

    public TodoItemBuilder WithId(int id)
    {
        _todoItem.Id = id;
        return this;
    }

    public TodoItemBuilder WithDescription(string description)
    {
        _todoItem.Description = description;
        return this;
    }

    public TodoItemBuilder WithIsCompleted(bool isCompleted)
    {
        _todoItem.IsCompleted = isCompleted;
        return this;
    }

    public TodoItemBuilder WithTodoListId(long todoListId)
    {
        _todoItem.TodoListId = todoListId;
        return this;
    }

    public TodoItemBuilder WithExternalId(string externalId)
    {
        _todoItem.ExternalId = externalId;
        return this;
    }

    public TodoItemBuilder WithLastModified(DateTime lastModified)
    {
        _todoItem.LastModified = lastModified;
        return this;
    }

    public TodoItemBuilder WithLastSyncedAt(DateTime lastSyncedAt)
    {
        _todoItem.LastSyncedAt = lastSyncedAt;
        return this;
    }

    public TodoItem Build() => _todoItem;

    public static TodoItemBuilder Create() => new TodoItemBuilder();
}
