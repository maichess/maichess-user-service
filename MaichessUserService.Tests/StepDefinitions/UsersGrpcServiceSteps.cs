using MaichessUserService.Tests.Support;
using Reqnroll;
using Xunit;

namespace MaichessUserService.Tests.StepDefinitions;

[Binding]
internal sealed class UsersGrpcServiceSteps(GrpcServiceContext context)
{
    // ── Given ────────────────────────────────────────────────────────────────

    [Given(@"a user exists with id ""([^""]*)"" and username ""([^""]*)""")]
    public void GivenAUserExistsWithIdAndUsername(string userId, string username)
    {
        context.SeedUser(userId, username);
    }

    [Given(@"the database signals a unique constraint violation on next save")]
    public void GivenTheDatabaseSignalsUniqueConstraintViolation()
    {
        context.UseConflictingService = true;
    }

    // ── When (CreateUser) ────────────────────────────────────────────────────

    [When(@"a user is created with username ""([^""]*)"" and password hash ""([^""]*)""")]
    public async Task WhenAUserIsCreatedWithUsernameAndPasswordHash(string username, string passwordHash)
    {
        context.CreateUserResult = await context.ActiveService.CreateUserAsync(
            username, passwordHash, CancellationToken.None);
    }

    // ── When (GetUser) ───────────────────────────────────────────────────────

    [When(@"user ""([^""]*)"" is retrieved")]
    public async Task WhenUserIsRetrieved(string userId)
    {
        context.GetUserResult = await context.ActiveService.GetUserAsync(userId, CancellationToken.None);
    }

    // ── When (UpdateUser) ────────────────────────────────────────────────────

    [When(@"user ""([^""]*)"" username is updated to ""([^""]*)""")]
    public async Task WhenUserUsernameIsUpdatedTo(string userId, string newUsername)
    {
        context.UpdateUserResult = await context.ActiveService.UpdateUserAsync(
            userId, newUsername, CancellationToken.None);
    }

    // ── Then (CreateUser results) ─────────────────────────────────────────────

    [Then(@"the create result is success with username ""([^""]*)""")]
    public void ThenCreateResultIsSuccessWithUsername(string username)
    {
        var result = Assert.IsType<CreateUserResult.Success>(context.CreateUserResult);
        Assert.Equal(username, result.User.Username);
    }

    [Then(@"the created user has Elo (\d+)")]
    public void ThenTheCreatedUserHasElo(int elo)
    {
        var result = Assert.IsType<CreateUserResult.Success>(context.CreateUserResult);
        Assert.Equal(elo, result.User.Elo);
    }

    [Then(@"the created user has zero wins losses and draws")]
    public void ThenTheCreatedUserHasZeroWinsLossesAndDraws()
    {
        var result = Assert.IsType<CreateUserResult.Success>(context.CreateUserResult);
        Assert.Equal(0, result.User.Wins);
        Assert.Equal(0, result.User.Losses);
        Assert.Equal(0, result.User.Draws);
    }

    [Then(@"the create result is invalid input ""([^""]*)""")]
    public void ThenCreateResultIsInvalidInput(string message)
    {
        var result = Assert.IsType<CreateUserResult.InvalidInput>(context.CreateUserResult);
        Assert.Equal(message, result.Message);
    }

    [Then(@"the create result is conflict")]
    public void ThenCreateResultIsConflict()
    {
        Assert.IsType<CreateUserResult.Conflict>(context.CreateUserResult);
    }

    [Then(@"the database insert stored password hash ""([^""]*)"" under the ""([^""]*)"" field")]
    public void ThenDatabaseInsertStoredPasswordHashUnderField(string expectedHash, string fieldName)
    {
        Assert.NotNull(context.LastInsertRequest);
        Assert.True(
            context.LastInsertRequest.Record.Fields.TryGetValue(fieldName, out var value),
            $"InsertRequest record had no '{fieldName}' field");
        Assert.Equal(expectedHash, value.StringValue);
    }

    // ── Then (GetUser results) ───────────────────────────────────────────────

    [Then(@"the get result is success with username ""([^""]*)""")]
    public void ThenGetResultIsSuccessWithUsername(string username)
    {
        var result = Assert.IsType<GetUserResult.Success>(context.GetUserResult);
        Assert.Equal(username, result.User.Username);
    }

    [Then(@"the get result is success with id ""([^""]*)""")]
    public void ThenGetResultIsSuccessWithId(string userId)
    {
        var result = Assert.IsType<GetUserResult.Success>(context.GetUserResult);
        Assert.Equal(userId, result.User.Id);
    }

    [Then(@"the get result is invalid user id")]
    public void ThenGetResultIsInvalidUserId()
    {
        Assert.IsType<GetUserResult.InvalidUserId>(context.GetUserResult);
    }

    [Then(@"the get result is not found")]
    public void ThenGetResultIsNotFound()
    {
        Assert.IsType<GetUserResult.NotFound>(context.GetUserResult);
    }

    // ── Then (UpdateUser results) ─────────────────────────────────────────────

    [Then(@"the update result is success with username ""([^""]*)""")]
    public void ThenUpdateResultIsSuccessWithUsername(string username)
    {
        var result = Assert.IsType<UpdateUserResult.Success>(context.UpdateUserResult);
        Assert.Equal(username, result.User.Username);
    }

    [Then(@"the update result is invalid input ""([^""]*)""")]
    public void ThenUpdateResultIsInvalidInput(string message)
    {
        var result = Assert.IsType<UpdateUserResult.InvalidInput>(context.UpdateUserResult);
        Assert.Equal(message, result.Message);
    }

    [Then(@"the update result is not found")]
    public void ThenUpdateResultIsNotFound()
    {
        Assert.IsType<UpdateUserResult.NotFound>(context.UpdateUserResult);
    }

    [Then(@"the update result is conflict")]
    public void ThenUpdateResultIsConflict()
    {
        Assert.IsType<UpdateUserResult.Conflict>(context.UpdateUserResult);
    }
}
