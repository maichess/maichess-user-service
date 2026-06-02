Feature: Record Match Result
  The user service records a finished match outcome for a player by
  incrementing the matching win, loss, or draw counter and applying a Glicko-2
  rating update against the supplied opponent.

  Scenario: Recording a win against a strong opponent raises the rating and shrinks the deviation
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" rating 400 deviation 350
    When a "win" result is recorded for user "00000000-0000-0000-0000-000000000001" against opponent rating 1500 deviation 50
    Then the record result is success
    And the recorded user rating is above 400
    And the recorded user deviation is below 350
    And the recorded user elo equals its rounded rating
    And the database update wrote the "rating" field
    And the database update wrote the "rating_deviation" field
    And the database update wrote the "volatility" field
    And the database update wrote the "elo" field

  Scenario: Recording a loss against a weak opponent lowers the rating
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" rating 1500 deviation 200
    When a "loss" result is recorded for user "00000000-0000-0000-0000-000000000001" against opponent rating 400 deviation 50
    Then the record result is success
    And the recorded user rating is below 1500

  Scenario: A legacy user with no rating fields is rated from their stored elo
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" wins 0 losses 0 draws 0
    When a "win" result is recorded for user "00000000-0000-0000-0000-000000000001" against opponent rating 1500 deviation 50
    Then the record result is success
    And the recorded user rating is above 1200

  Scenario: Recording a win increments only the wins counter
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" wins 3 losses 2 draws 1
    When a "win" result is recorded for user "00000000-0000-0000-0000-000000000001"
    Then the record result is success
    And the recorded user has wins 4 losses 2 draws 1
    And the database update set the "wins" field to 4

  Scenario: Recording a loss increments only the losses counter
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" wins 3 losses 2 draws 1
    When a "loss" result is recorded for user "00000000-0000-0000-0000-000000000001"
    Then the record result is success
    And the recorded user has wins 3 losses 3 draws 1
    And the database update set the "losses" field to 3

  Scenario: Recording a draw increments only the draws counter
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" wins 3 losses 2 draws 1
    When a "draw" result is recorded for user "00000000-0000-0000-0000-000000000001"
    Then the record result is success
    And the recorded user has wins 3 losses 2 draws 2
    And the database update set the "draws" field to 2

  Scenario: Recording for a non-UUID id fails validation
    When a "win" result is recorded for user "not-a-uuid"
    Then the record result is invalid input "user_id must be a valid UUID"

  Scenario: Recording an unspecified outcome fails validation
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" wins 0 losses 0 draws 0
    When an "unspecified" result is recorded for user "00000000-0000-0000-0000-000000000001"
    Then the record result is invalid input "outcome is required"

  Scenario: Recording for a user that does not exist fails
    When a "win" result is recorded for user "00000000-0000-0000-0000-000000000099"
    Then the record result is not found
