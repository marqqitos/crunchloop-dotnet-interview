# TodoListApi

All URIs are relative to *http://localhost*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**createTodoList**](TodoListApi.md#createTodoList) | **POST** /todolists | Create a new TodoList with items |
| [**deleteTodoList**](TodoListApi.md#deleteTodoList) | **DELETE** /todolists/{todolistId} | Delete a TodoList and its items |
| [**listTodoLists**](TodoListApi.md#listTodoLists) | **GET** /todolists | Fetch all TodoLists and their items |
| [**updateTodoList**](TodoListApi.md#updateTodoList) | **PATCH** /todolists/{todolistId} | Update a TodoList |


<a name="createTodoList"></a>
# **createTodoList**
> TodoList createTodoList(CreateTodoListBody)

Create a new TodoList with items

    Creates a new TodoList along with associated TodoItems.

### Parameters

|Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **CreateTodoListBody** | [**CreateTodoListBody**](../Models/CreateTodoListBody.md)|  | |

### Return type

[**TodoList**](../Models/TodoList.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: application/json
- **Accept**: application/json

<a name="deleteTodoList"></a>
# **deleteTodoList**
> deleteTodoList(todolistId)

Delete a TodoList and its items

    Deletes an existing TodoList and all its associated TodoItems.

### Parameters

|Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **todolistId** | **String**| The unique identifier of the TodoList to delete. | [default to null] |

### Return type

null (empty response body)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: Not defined

<a name="listTodoLists"></a>
# **listTodoLists**
> List listTodoLists()

Fetch all TodoLists and their items

    Retrieves all TodoLists along with their associated TodoItems.

### Parameters
This endpoint does not need any parameter.

### Return type

[**List**](../Models/TodoList.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: application/json

<a name="updateTodoList"></a>
# **updateTodoList**
> TodoList updateTodoList(todolistId, UpdateTodoListBody)

Update a TodoList

    Updates an existing TodoList.

### Parameters

|Name | Type | Description  | Notes |
|------------- | ------------- | ------------- | -------------|
| **todolistId** | **String**| The unique identifier of the TodoList to update. | [default to null] |
| **UpdateTodoListBody** | [**UpdateTodoListBody**](../Models/UpdateTodoListBody.md)|  | |

### Return type

[**TodoList**](../Models/TodoList.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: application/json
- **Accept**: application/json

