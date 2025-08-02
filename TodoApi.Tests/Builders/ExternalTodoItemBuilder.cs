namespace TodoApi.Tests.Builders;

public class ExternalTodoItemBuilder
{
    private ExternalTodoItem _externalTodoItem = new ExternalTodoItem();

    public ExternalTodoItemBuilder WithId(string id)
    {
        _externalTodoItem.Id = id;
        return this;
    }

    public ExternalTodoItemBuilder WithSourceId(string sourceId)
    {
        _externalTodoItem.SourceId = sourceId;
        return this;
    }

    public ExternalTodoItemBuilder WithDescription(string description)
    {
        _externalTodoItem.Description = description;
        return this;
    }

    public ExternalTodoItemBuilder WithCompleted(bool completed)
    {
        _externalTodoItem.Completed = completed;
        return this;
    }

    public ExternalTodoItemBuilder WithCreatedAt(DateTime createdAt)
    {
        _externalTodoItem.CreatedAt = createdAt;
        return this;
    }

    public ExternalTodoItemBuilder WithUpdatedAt(DateTime updatedAt)
    {
        _externalTodoItem.UpdatedAt = updatedAt;
        return this;
    }

    public ExternalTodoItem Build() => _externalTodoItem;

    public static ExternalTodoItemBuilder Create() => new ExternalTodoItemBuilder();
}
