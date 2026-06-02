namespace MaichessUserService.Rating;

// A player's rating on the Glicko-2 display scale.
internal readonly record struct RatingState(double Rating, double RatingDeviation, double Volatility);
