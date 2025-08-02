using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Controllers;
using TodoApi.Dtos;
using TodoApi.Tests.Builders;

namespace TodoApi.Tests;

#nullable disable
public class TodoItemsControllerTests
{
    private DbContextOptions<TodoContext> DatabaseContextOptions()
    {
        return new DbContextOptionsBuilder<TodoContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private void PopulateDatabaseContext(TodoContext context)
    {
        context.TodoList.Add(TodoListBuilder.Create()
            .WithName("Work Tasks")
            .Build());
        context.TodoList.Add(TodoListBuilder.Create()
            .WithName("Personal Tasks")
            .Build());
        
        context.TodoItem.Add(TodoItemBuilder.Create()
            .WithDescription("Task 1")
            .WithCompleted(false)
            .WithTodoListId(1)
            .Build());
        context.TodoItem.Add(TodoItemBuilder.Create()
            .WithDescription("Task 2")
            .WithCompleted(true)
            .WithTodoListId(1)
            .Build());
        context.TodoItem.Add(TodoItemBuilder.Create()
            .WithDescription("Task 3")
            .WithCompleted(false)
            .WithTodoListId(2)
            .Build());
        
        context.SaveChanges();
    }

    [Fact]
    public async Task GetTodoItems_WhenTodoListDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.GetTodoItems(999);

            Assert.IsType<NotFoundObjectResult>(result.Result);
            var notFoundResult = result.Result as NotFoundObjectResult;
            Assert.Equal("TodoList with id 999 not found", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task GetTodoItems_WhenTodoListExistsWithNoItems_ReturnsEmptyList()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            context.TodoList.Add(new TodoList { Id = 3, Name = "Empty List" });
            context.SaveChanges();

            var controller = new TodoItemsController(context);

            var result = await controller.GetTodoItems(3);

            Assert.IsType<OkObjectResult>(result.Result);
            var okResult = result.Result as OkObjectResult;
            var items = okResult.Value as IList<TodoItemResponse>;
            Assert.Empty(items);
        }
    }

    [Fact]
    public async Task GetTodoItems_WhenTodoListExistsWithItems_ReturnsAllItems()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.GetTodoItems(1);

            Assert.IsType<OkObjectResult>(result.Result);
            var okResult = result.Result as OkObjectResult;
            var items = okResult.Value as IList<TodoItemResponse>;
            Assert.Equal(2, items.Count);
            Assert.Equal("Task 1", items[0].Description);
            Assert.Equal("Task 2", items[1].Description);
        }
    }

    [Fact]
    public async Task GetTodoItem_WhenTodoListDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.GetTodoItem(999, 1);

            Assert.IsType<NotFoundObjectResult>(result.Result);
            var notFoundResult = result.Result as NotFoundObjectResult;
            Assert.Equal("TodoList with id 999 not found", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task GetTodoItem_WhenTodoItemDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.GetTodoItem(1, 999);

            Assert.IsType<NotFoundObjectResult>(result.Result);
            var notFoundResult = result.Result as NotFoundObjectResult;
            Assert.Equal("TodoItem with id 999 not found in TodoList 1", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task GetTodoItem_WhenTodoItemExistsInDifferentList_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.GetTodoItem(2, 1); // Item 1 is in TodoList 1, not 2

            Assert.IsType<NotFoundObjectResult>(result.Result);
            var notFoundResult = result.Result as NotFoundObjectResult;
            Assert.Equal("TodoItem with id 1 not found in TodoList 2", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task GetTodoItem_WhenTodoItemExists_ReturnsTodoItem()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.GetTodoItem(1, 1);

            Assert.IsType<OkObjectResult>(result.Result);
            var okResult = result.Result as OkObjectResult;
            var item = okResult.Value as TodoItemResponse;
            Assert.Equal(1, item.Id);
            Assert.Equal("Task 1", item.Description);
            Assert.False(item.Completed);
            Assert.Equal(1, item.TodoListId);
        }
    }

    [Fact]
    public async Task PutTodoItem_WhenTodoListDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);
            var updatePayload = new UpdateTodoItem { Description = "Updated Task", Completed = true };

            var result = await controller.PutTodoItem(999, 1, updatePayload);

            Assert.IsType<NotFoundObjectResult>(result.Result);
            var notFoundResult = result.Result as NotFoundObjectResult;
            Assert.Equal("TodoList with id 999 not found", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task PutTodoItem_WhenTodoItemDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);
            var updatePayload = new UpdateTodoItem { Description = "Updated Task", Completed = true };

            var result = await controller.PutTodoItem(1, 999, updatePayload);

            Assert.IsType<NotFoundObjectResult>(result.Result);
            var notFoundResult = result.Result as NotFoundObjectResult;
            Assert.Equal("TodoItem with id 999 not found in TodoList 1", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task PutTodoItem_WhenTodoItemExistsInDifferentList_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);
            var updatePayload = new UpdateTodoItem { Description = "Updated Task", Completed = true };

            var result = await controller.PutTodoItem(2, 1, updatePayload); // Item 1 is in TodoList 1, not 2

            Assert.IsType<NotFoundObjectResult>(result.Result);
            var notFoundResult = result.Result as NotFoundObjectResult;
            Assert.Equal("TodoItem with id 1 not found in TodoList 2", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task PutTodoItem_WhenTodoItemExists_UpdatesTodoItem()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);
            var updatePayload = new UpdateTodoItem { Description = "Updated Task 1", Completed = true };

            var result = await controller.PutTodoItem(1, 1, updatePayload);

            Assert.IsType<OkObjectResult>(result.Result);
            var okResult = result.Result as OkObjectResult;
            var updatedItem = okResult.Value as TodoItemResponse;
            
            Assert.Equal(1, updatedItem.Id);
            Assert.Equal("Updated Task 1", updatedItem.Description);
            Assert.True(updatedItem.Completed);
            Assert.Equal(1, updatedItem.TodoListId);

            // Verify the item was actually updated in the database
            var dbItem = await context.TodoItem.FindAsync(1L);
            Assert.Equal("Updated Task 1", dbItem.Description);
            Assert.True(dbItem.IsCompleted);
        }
    }

    [Fact]
    public async Task PostTodoItem_WhenTodoListDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);
            var createPayload = new CreateTodoItem { Description = "New Task", Completed = false };

            var result = await controller.PostTodoItem(999, createPayload);

            Assert.IsType<NotFoundObjectResult>(result.Result);
            var notFoundResult = result.Result as NotFoundObjectResult;
            Assert.Equal("TodoList with id 999 not found", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task PostTodoItem_WhenTodoListExists_CreatesTodoItem()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);
            var createPayload = new CreateTodoItem { Description = "New Task", Completed = false };

            var result = await controller.PostTodoItem(1, createPayload);

            Assert.IsType<CreatedAtActionResult>(result.Result);
            var createdResult = result.Result as CreatedAtActionResult;
            var createdItem = createdResult.Value as TodoItemResponse;
            
            Assert.Equal("New Task", createdItem.Description);
            Assert.False(createdItem.Completed);
            Assert.Equal(1, createdItem.TodoListId);
            Assert.True(createdItem.Id > 0); // Should have generated ID

            // Verify the item was actually created in the database
            Assert.Equal(4, context.TodoItem.Count()); // Originally had 3, now should have 4
            var dbItem = await context.TodoItem.FindAsync(createdItem.Id);
            Assert.NotNull(dbItem);
            Assert.Equal("New Task", dbItem.Description);
        }
    }

    [Fact]
    public async Task PostTodoItem_WhenTodoListExists_CreatesCompletedTodoItem()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);
            var createPayload = new CreateTodoItem { Description = "Completed Task", Completed = true };

            var result = await controller.PostTodoItem(2, createPayload);

            Assert.IsType<CreatedAtActionResult>(result.Result);
            var createdResult = result.Result as CreatedAtActionResult;
            var createdItem = createdResult.Value as TodoItemResponse;
            
            Assert.Equal("Completed Task", createdItem.Description);
            Assert.True(createdItem.Completed);
            Assert.Equal(2, createdItem.TodoListId);
        }
    }

    [Fact]
    public async Task DeleteTodoItem_WhenTodoListDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.DeleteTodoItem(999, 1);

            Assert.IsType<NotFoundObjectResult>(result);
            var notFoundResult = result as NotFoundObjectResult;
            Assert.Equal("TodoList with id 999 not found", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task DeleteTodoItem_WhenTodoItemDoesntExist_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.DeleteTodoItem(1, 999);

            Assert.IsType<NotFoundObjectResult>(result);
            var notFoundResult = result as NotFoundObjectResult;
            Assert.Equal("TodoItem with id 999 not found in TodoList 1", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task DeleteTodoItem_WhenTodoItemExistsInDifferentList_ReturnsNotFound()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.DeleteTodoItem(2, 1); // Item 1 is in TodoList 1, not 2

            Assert.IsType<NotFoundObjectResult>(result);
            var notFoundResult = result as NotFoundObjectResult;
            Assert.Equal("TodoItem with id 1 not found in TodoList 2", notFoundResult.Value);
        }
    }

    [Fact]
    public async Task DeleteTodoItem_WhenTodoItemExists_DeletesTodoItem()
    {
        using (var context = new TodoContext(DatabaseContextOptions()))
        {
            PopulateDatabaseContext(context);

            var controller = new TodoItemsController(context);

            var result = await controller.DeleteTodoItem(1, 1);

            Assert.IsType<NoContentResult>(result);
            
            // Verify the item was actually deleted from the database
            Assert.Equal(2, context.TodoItem.Count()); // Originally had 3, now should have 2
            var deletedItem = await context.TodoItem.FindAsync(1L);
            Assert.Null(deletedItem);
        }
    }
}