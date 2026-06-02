using MaichessUserService.Rating;
using Xunit;

namespace MaichessUserService.Tests.Rating;

// Exhaustive unit tests for the pure Glicko-2 module. The canonical reference is
// Glickman's "Example of the Glicko-2 system" worked example.
public sealed class Glicko2Tests
{
    // The worked example: a 1500/200/0.06 player over one rating period against
    // three opponents, scoring 1, 0, 0. Glickman's published results are
    // r' ≈ 1464.06, RD' ≈ 151.52, σ' ≈ 0.05999.
    [Fact]
    public void Update_MatchesGlickmanWorkedExample()
    {
        RatingState start = new(1500.0, 200.0, 0.06);
        RatingGame[] games =
        [
            new RatingGame(1400.0, 30.0, 1.0),
            new RatingGame(1550.0, 100.0, 0.0),
            new RatingGame(1700.0, 300.0, 0.0),
        ];

        RatingState result = Glicko2.Update(start, games);

        // Glickman publishes r' ≈ 1464.06, RD' ≈ 151.52, σ' ≈ 0.05999 (rounded);
        // assert against the exact values with a small tolerance.
        Assert.Equal(1464.06, result.Rating, 0.01);
        Assert.Equal(151.52, result.RatingDeviation, 0.01);
        Assert.Equal(0.05999, result.Volatility, 0.00001);
    }

    [Theory]
    [InlineData(30.0, 0.9955)]
    [InlineData(100.0, 0.9531)]
    [InlineData(300.0, 0.7242)]
    public void G_MatchesWorkedExampleValues(double rd, double expected)
    {
        double phi = rd / 173.7178;
        Assert.Equal(expected, Glicko2.G(phi), 4);
    }

    [Theory]
    [InlineData(1400.0, 30.0, 0.639)]
    [InlineData(1550.0, 100.0, 0.432)]
    [InlineData(1700.0, 300.0, 0.303)]
    public void E_MatchesWorkedExampleValues(double opponentRating, double opponentRd, double expected)
    {
        // μ = 0 corresponds to the worked example's 1500-rated player.
        double muJ = (opponentRating - 1500.0) / 173.7178;
        double phiJ = opponentRd / 173.7178;
        Assert.Equal(expected, Glicko2.E(0.0, muJ, phiJ), 3);
    }

    [Fact]
    public void Update_WithNoGames_OnlyInflatesDeviation()
    {
        RatingState start = new(400.0, 200.0, 0.06);

        RatingState result = Glicko2.Update(start, []);

        Assert.Equal(400.0, result.Rating);
        Assert.Equal(0.06, result.Volatility);
        Assert.True(result.RatingDeviation > 200.0, "an idle period must inflate the deviation");
    }

    [Fact]
    public void Update_WinAgainstEqualOpponent_RaisesRatingAndShrinksDeviation()
    {
        RatingState start = new(1500.0, 200.0, 0.06);

        RatingState result = Glicko2.Update(start, [new RatingGame(1500.0, 200.0, 1.0)]);

        Assert.True(result.Rating > 1500.0);
        Assert.True(result.RatingDeviation < 200.0);
    }

    [Fact]
    public void Update_LossAgainstEqualOpponent_LowersRating()
    {
        RatingState start = new(1500.0, 200.0, 0.06);

        RatingState result = Glicko2.Update(start, [new RatingGame(1500.0, 200.0, 0.0)]);

        Assert.True(result.Rating < 1500.0);
    }

    [Fact]
    public void Update_DrawAgainstEqualOpponent_BarelyMovesRating()
    {
        RatingState start = new(1500.0, 200.0, 0.06);

        RatingState result = Glicko2.Update(start, [new RatingGame(1500.0, 200.0, 0.5)]);

        Assert.Equal(1500.0, result.Rating, 6);
        Assert.True(result.RatingDeviation < 200.0);
    }

    [Fact]
    public void Update_ProvisionalPlayer_MovesMoreThanEstablishedPlayer()
    {
        RatingGame game = new(1500.0, 50.0, 1.0);

        RatingState provisional = Glicko2.Update(new RatingState(1500.0, 350.0, 0.06), [game]);
        RatingState established = Glicko2.Update(new RatingState(1500.0, 50.0, 0.06), [game]);

        Assert.True(
            provisional.Rating - 1500.0 > established.Rating - 1500.0,
            "a high-RD provisional player should move further on the same result");
    }

    // A massive upset (a tightly-rated player beats a far stronger opponent)
    // drives Δ² above φ² + v, exercising the closed-form upper-bracket branch of
    // the volatility solver.
    [Fact]
    public void Update_MassiveUpset_TakesClosedFormVolatilityBracket()
    {
        RatingState start = new(1500.0, 30.0, 0.06);

        RatingState result = Glicko2.Update(start, [new RatingGame(2800.0, 30.0, 1.0)]);

        Assert.True(result.Rating > 1500.0);
        Assert.True(result.Volatility > 0.06, "a shock result should raise volatility");
    }

    // The bracket-expansion loop only iterates when tau > 2 (otherwise the
    // 1/tau² term dominates). Driving the solver directly with a large tau and a
    // high seed volatility forces at least one expansion step.
    [Fact]
    public void ComputeNewVolatility_LargeTau_ExpandsLowerBracket()
    {
        double result = Glicko2.ComputeNewVolatility(phi: 0.0, v: 4.0, delta: 0.0, sigma: 48.9, tau: 5.0);

        Assert.True(result > 0.0);
        Assert.False(double.IsNaN(result));
    }

    [Fact]
    public void ComputeNewVolatility_ConvergesForWorkedExample()
    {
        // φ = 200/173.7178, v and Δ from the worked example.
        double result = Glicko2.ComputeNewVolatility(
            phi: 200.0 / 173.7178, v: 1.7785, delta: -0.4834, sigma: 0.06, tau: 0.5);

        Assert.Equal(0.05999, result, 0.00001);
    }
}
