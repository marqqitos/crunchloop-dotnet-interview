namespace TodoApi.Tests.Builders;

public class ExternalTodoListBuilder
{
    private ExternalTodoList _externalTodoList = new ExternalTodoList();

    public ExternalTodoListBuilder WithId(string id)
    {
        _externalTodoList.Id = id;
        return this;
    }

    public ExternalTodoListBuilder WithSourceId(string sourceId)
    {
        _externalTodoList.SourceId = sourceId;
        return this;
    }

    public ExternalTodoListBuilder WithName(string name)
    {
        _externalTodoList.Name = name;
        return this;
    }

    public ExternalTodoListBuilder WithCreatedAt(DateTime createdAt)
    {
        _externalTodoList.CreatedAt = createdAt;
        return this;
    }

    public ExternalTodoListBuilder WithUpdatedAt(DateTime updatedAt)
    {
        _externalTodoList.UpdatedAt = updatedAt;
        return this;
    }

    public ExternalTodoListBuilder WithItem(ExternalTodoItem item)
	{
		_externalTodoList.Items.Add(item);
		return this;
	}

    public ExternalTodoList Build() => _externalTodoList;

    public static ExternalTodoListBuilder Create() => new ExternalTodoListBuilder();
}
