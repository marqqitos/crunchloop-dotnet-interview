# TodoItemApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**deleteTodoItem**](TodoItemApi.md#deleteTodoItem) | **DELETE** /todolists/{todolistId}/todoitems/{todoitemId} | Delete a TodoItem |
| [**updateTodoItem**](TodoItemApi.md#updateTodoItem) | **PATCH** /todolists/{todolistId}/todoitems/{todoitemId} | Update a TodoItem |


<a name="deleteTodoItem"></a>
# **deleteTodoItem**
> deleteTodoItem(todolistId, todoitemId)

Delete a TodoItem

    Deletes an existing TodoItem within a specific TodoList.

### Parameters

|Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **todolistId** | **String**| The unique identifier of the TodoList containing the TodoItem. | [default to null] |
| **todoitemId** | **String**| The unique identifier of the TodoItem to delete. | [default to null] |

### Return type

null (empty response body)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: Not defined

<a name="updateTodoItem"></a>
# **updateTodoItem**
> TodoItem updateTodoItem(todolistId, todoitemId, UpdateTodoItemBody)

Update a TodoItem

    Updates an existing TodoItem within a specific TodoList.

### Parameters

|Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **todolistId** | **String**| The unique identifier of the TodoList containing the TodoItem. | [default to null] |
| **todoitemId** | **String**| The unique identifier of the TodoItem to update. | [default to null] |
| **UpdateTodoItemBody** | [**UpdateTodoItemBody**](../Models/UpdateTodoItemBody.md)|  | |

### Return type

[**TodoItem**](../Models/TodoItem.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: application/json
- **Accept**: application/json

