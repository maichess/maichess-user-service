using System.Globalization;
using Maichess.User.V1;
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

    [Given(@"a user exists with id ""([^""]*)"" username ""([^""]*)"" wins (\d+) losses (\d+) draws (\d+)")]
    public void GivenAUserExistsWithStats(string userId, string username, int wins, int losses, int draws)
    {
        context.SeedUser(userId, username, false, wins, losses, draws);
    }

    [Given(@"a user exists with id ""([^""]*)"" username ""([^""]*)"" rating (\d+) deviation (\d+)")]
    public void GivenAUserExistsWithRating(string userId, string username, int rating, int deviation)
    {
        context.SeedUser(
            userId, username, false, 0, 0, 0, rating, deviation, 0.06);
    }

    [Given(@"a user exists with id ""([^""]*)"" username ""([^""]*)"" and dev_mode (true|false)")]
    public void GivenAUserExistsWithDevMode(string userId, string username, string devMode)
    {
        context.SeedUser(userId, username, bool.Parse(devMode));
    }

    [Given(@"a legacy user exists with id ""([^""]*)"" and username ""([^""]*)"" with no dev_mode field")]
    public void GivenALegacyUserExists(string userId, string username)
    {
        context.SeedUser(userId, username, null);
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
            userId, newUsername, null, CancellationToken.None);
    }

    [When(@"user ""([^""]*)"" dev_mode is updated to (true|false)")]
    public async Task WhenUserDevModeIsUpdatedTo(string userId, string devMode)
    {
        context.UpdateUserResult = await context.ActiveService.UpdateUserAsync(
            userId, null, bool.Parse(devMode), CancellationToken.None);
    }

    [When(@"user ""([^""]*)"" username is updated to ""([^""]*)"" and dev_mode to (true|false)")]
    public async Task WhenUserUsernameAndDevModeAreUpdated(string userId, string newUsername, string devMode)
    {
        context.UpdateUserResult = await context.ActiveService.UpdateUserAsync(
            userId, newUsername, bool.Parse(devMode), CancellationToken.None);
    }

    [When(@"user ""([^""]*)"" is updated with no fields")]
    public async Task WhenUserIsUpdatedWithNoFields(string userId)
    {
        context.UpdateUserResult = await context.ActiveService.UpdateUserAsync(
            userId, null, null, CancellationToken.None);
    }

    // ── When (RecordMatchResult) ─────────────────────────────────────────────

    [When(@"an? ""([^""]*)"" result is recorded for user ""([^""]*)""")]
    public async Task WhenAResultIsRecordedForUser(string outcome, string userId)
    {
        context.RecordMatchResultResult = await context.ActiveService.RecordMatchResultAsync(
            userId, ParseOutcome(outcome), 1500.0, 350.0, CancellationToken.None);
    }

    [When(@"an? ""([^""]*)"" result is recorded for user ""([^""]*)"" against opponent rating (\d+) deviation (\d+)")]
    public async Task WhenAResultIsRecordedAgainstOpponent(
        string outcome, string userId, int opponentRating, int opponentRd)
    {
        context.RecordMatchResultResult = await context.ActiveService.RecordMatchResultAsync(
            userId, ParseOutcome(outcome), opponentRating, opponentRd, CancellationToken.None);
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

    [Then(@"^the created user has dev_mode (true|false)$")]
    public void ThenTheCreatedUserHasDevMode(string devMode)
    {
        var result = Assert.IsType<CreateUserResult.Success>(context.CreateUserResult);
        Assert.Equal(bool.Parse(devMode), result.User.DevMode);
    }

    [Then(@"the created user has rating (\d+) deviation (\d+) volatility ""([^""]*)""")]
    public void ThenTheCreatedUserHasRating(int rating, int deviation, string volatility)
    {
        var result = Assert.IsType<CreateUserResult.Success>(context.CreateUserResult);
        Assert.Equal(rating, result.User.Rating);
        Assert.Equal(deviation, result.User.RatingDeviation);
        Assert.Equal(double.Parse(volatility, CultureInfo.InvariantCulture), result.User.Volatility);
    }

    [Then(@"the database insert stored ([0-9.]+) under the ""([^""]*)"" field")]
    public void ThenDatabaseInsertStoredNumberUnderField(string expected, string fieldName)
    {
        Assert.NotNull(context.LastInsertRequest);
        Assert.True(
            context.LastInsertRequest.Record.Fields.TryGetValue(fieldName, out var value),
            $"InsertRequest record had no '{fieldName}' field");
        Assert.Equal(double.Parse(expected, CultureInfo.InvariantCulture), value.NumberValue);
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

    [Then(@"the database insert stored dev_mode (true|false) under the ""([^""]*)"" field")]
    public void ThenDatabaseInsertStoredDevModeUnderField(string expected, string fieldName)
    {
        Assert.NotNull(context.LastInsertRequest);
        Assert.True(
            context.LastInsertRequest.Record.Fields.TryGetValue(fieldName, out var value),
            $"InsertRequest record had no '{fieldName}' field");
        Assert.Equal(bool.Parse(expected), value.BoolValue);
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

    [Then(@"^the get result has dev_mode (true|false)$")]
    public void ThenGetResultHasDevMode(string devMode)
    {
        var result = Assert.IsType<GetUserResult.Success>(context.GetUserResult);
        Assert.Equal(bool.Parse(devMode), result.User.DevMode);
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

    [Then(@"^the update result has dev_mode (true|false)$")]
    public void ThenUpdateResultHasDevMode(string devMode)
    {
        var result = Assert.IsType<UpdateUserResult.Success>(context.UpdateUserResult);
        Assert.Equal(bool.Parse(devMode), result.User.DevMode);
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

    // ── Then (RecordMatchResult results) ──────────────────────────────────────

    [Then(@"the record result is success")]
    public void ThenRecordResultIsSuccess()
    {
        Assert.IsType<RecordMatchResultResult.Success>(context.RecordMatchResultResult);
    }

    [Then(@"the recorded user has wins (\d+) losses (\d+) draws (\d+)")]
    public void ThenRecordedUserHasStats(int wins, int losses, int draws)
    {
        var result = Assert.IsType<RecordMatchResultResult.Success>(context.RecordMatchResultResult);
        Assert.Equal(wins, result.User.Wins);
        Assert.Equal(losses, result.User.Losses);
        Assert.Equal(draws, result.User.Draws);
    }

    [Then(@"the database update set the ""([^""]*)"" field to (\d+)")]
    public void ThenDatabaseUpdateSetFieldTo(string fieldName, int expected)
    {
        Assert.NotNull(context.LastUpdateRequest);
        Assert.True(
            context.LastUpdateRequest.Fields.Fields.TryGetValue(fieldName, out var value),
            $"UpdateRequest fields had no '{fieldName}' field");
        Assert.Equal(expected, (int)value.NumberValue);
    }

    [Then(@"the recorded user rating is above (\d+)")]
    public void ThenRecordedUserRatingIsAbove(int floor)
    {
        var result = Assert.IsType<RecordMatchResultResult.Success>(context.RecordMatchResultResult);
        Assert.True(result.User.Rating > floor, $"expected rating > {floor} but was {result.User.Rating}");
    }

    [Then(@"the recorded user rating is below (\d+)")]
    public void ThenRecordedUserRatingIsBelow(int ceiling)
    {
        var result = Assert.IsType<RecordMatchResultResult.Success>(context.RecordMatchResultResult);
        Assert.True(result.User.Rating < ceiling, $"expected rating < {ceiling} but was {result.User.Rating}");
    }

    [Then(@"the recorded user deviation is below (\d+)")]
    public void ThenRecordedUserDeviationIsBelow(int ceiling)
    {
        var result = Assert.IsType<RecordMatchResultResult.Success>(context.RecordMatchResultResult);
        Assert.True(
            result.User.RatingDeviation < ceiling,
            $"expected deviation < {ceiling} but was {result.User.RatingDeviation}");
    }

    [Then(@"the recorded user elo equals its rounded rating")]
    public void ThenRecordedUserEloEqualsRoundedRating()
    {
        var result = Assert.IsType<RecordMatchResultResult.Success>(context.RecordMatchResultResult);
        Assert.Equal((int)Math.Round(result.User.Rating), result.User.Elo);
    }

    [Then(@"the database update wrote the ""([^""]*)"" field")]
    public void ThenDatabaseUpdateWroteField(string fieldName)
    {
        Assert.NotNull(context.LastUpdateRequest);
        Assert.True(
            context.LastUpdateRequest.Fields.Fields.ContainsKey(fieldName),
            $"UpdateRequest fields had no '{fieldName}' field");
    }

    [Then(@"the record result is invalid input ""([^""]*)""")]
    public void ThenRecordResultIsInvalidInput(string message)
    {
        var result = Assert.IsType<RecordMatchResultResult.InvalidInput>(context.RecordMatchResultResult);
        Assert.Equal(message, result.Message);
    }

    [Then(@"the record result is not found")]
    public void ThenRecordResultIsNotFound()
    {
        Assert.IsType<RecordMatchResultResult.NotFound>(context.RecordMatchResultResult);
    }

    private static MatchOutcome ParseOutcome(string outcome) => outcome switch
    {
        "win" => MatchOutcome.Win,
        "loss" => MatchOutcome.Loss,
        "draw" => MatchOutcome.Draw,
        _ => MatchOutcome.Unspecified,
    };
}
