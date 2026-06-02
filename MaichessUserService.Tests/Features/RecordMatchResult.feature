Feature: Record Match Result
  The user service records a finished match outcome for a player by
  incrementing the matching win, loss, or draw counter.

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
