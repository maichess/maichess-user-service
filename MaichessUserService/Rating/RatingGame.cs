namespace MaichessUserService.Rating;

// One game in a rating period: the opponent's display-scale rating and rating
// deviation, plus the score from the player's perspective (1 win, 0.5 draw, 0 loss).
internal readonly record struct RatingGame(double OpponentRating, double OpponentRd, double Score);
