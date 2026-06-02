namespace MaichessUserService.Rating;

// Pure implementation of Glicko-2 (Glickman, "Example of the Glicko-2 system").
// No I/O. The display scale is anchored at 1500 with spread 173.7178 (= 400/ln 10),
// the canonical transform; maichess merely seeds new players at 400 instead of 1500.
internal static class Glicko2
{
    internal const double DefaultRating = 400.0;
    internal const double DefaultRatingDeviation = 350.0;
    internal const double DefaultVolatility = 0.06;

    // System constant (τ): constrains volatility change over time.
    internal const double Tau = 0.5;

    // Convergence tolerance for the volatility solver (ε in the spec).
    private const double ConvergenceTolerance = 0.000001;

    private const double Scale = 173.7178;
    private const double Center = 1500.0;

    // Applies a rating period to the player. An empty period only inflates the
    // deviation (the player did not compete); otherwise the full step 3–8 update
    // runs. Returns the new display-scale state.
    internal static RatingState Update(RatingState state, IReadOnlyCollection<RatingGame> games)
    {
        double phi = state.RatingDeviation / Scale;
        double sigma = state.Volatility;

        if (games.Count == 0)
        {
            double idlePhi = Math.Sqrt((phi * phi) + (sigma * sigma));
            return new RatingState(state.Rating, idlePhi * Scale, sigma);
        }

        double mu = (state.Rating - Center) / Scale;

        // Steps 3 & 4: estimated variance v and the rating-improvement sum.
        double vInverse = 0.0;
        double improvementSum = 0.0;
        foreach (RatingGame game in games)
        {
            double muJ = (game.OpponentRating - Center) / Scale;
            double phiJ = game.OpponentRd / Scale;
            double g = G(phiJ);
            double e = E(mu, muJ, phiJ);
            vInverse += g * g * e * (1.0 - e);
            improvementSum += g * (game.Score - e);
        }

        double v = 1.0 / vInverse;
        double delta = v * improvementSum;

        // Step 5: new volatility via the Illinois (regula-falsi) iteration.
        double newSigma = ComputeNewVolatility(phi, v, delta, sigma, Tau);

        // Step 6: pre-period deviation inflated by the new volatility.
        double phiStar = Math.Sqrt((phi * phi) + (newSigma * newSigma));

        // Step 7: new deviation and rating on the internal scale.
        double newPhi = 1.0 / Math.Sqrt((1.0 / (phiStar * phiStar)) + (1.0 / v));
        double newMu = mu + (newPhi * newPhi * improvementSum);

        // Step 8: convert back to the display scale.
        return new RatingState((Scale * newMu) + Center, Scale * newPhi, newSigma);
    }

    // g(φ): weights an opponent's contribution by their deviation.
    internal static double G(double phi) => 1.0 / Math.Sqrt(1.0 + (3.0 * phi * phi / (Math.PI * Math.PI)));

    // E(μ, μ_j, φ_j): expected score against an opponent on the internal scale.
    internal static double E(double mu, double muJ, double phiJ) =>
        1.0 / (1.0 + Math.Exp(-G(phiJ) * (mu - muJ)));

    // The Illinois (regula-falsi) volatility solver. Exposed internally (and with
    // tau as a parameter) so the bracket-expansion loop — only reachable for
    // tau > 2 — can be exercised directly; Update always calls it with Tau.
    internal static double ComputeNewVolatility(double phi, double v, double delta, double sigma, double tau)
    {
        double deltaSq = delta * delta;
        double phiSq = phi * phi;
        double a = Math.Log(sigma * sigma);

        double F(double x)
        {
            double ex = Math.Exp(x);
            double numerator = ex * (deltaSq - phiSq - v - ex);
            double denominator = 2.0 * (phiSq + v + ex) * (phiSq + v + ex);
            return (numerator / denominator) - ((x - a) / (tau * tau));
        }

        double lower = a;
        double upper;
        if (deltaSq > phiSq + v)
        {
            upper = Math.Log(deltaSq - phiSq - v);
        }
        else
        {
            int k = 1;
            while (F(a - (k * tau)) < 0.0)
            {
                k++;
            }

            upper = a - (k * tau);
        }

        double fLower = F(lower);
        double fUpper = F(upper);

        while (Math.Abs(upper - lower) > ConvergenceTolerance)
        {
            double mid = lower + ((lower - upper) * fLower / (fUpper - fLower));
            double fMid = F(mid);
            if (fMid * fUpper <= 0.0)
            {
                lower = upper;
                fLower = fUpper;
            }
            else
            {
                fLower /= 2.0;
            }

            upper = mid;
            fUpper = fMid;
        }

        return Math.Exp(lower / 2.0);
    }
}
