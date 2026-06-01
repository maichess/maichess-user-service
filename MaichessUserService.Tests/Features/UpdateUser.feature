Feature: Update User
  The user service updates a user's username via gRPC.

  Scenario: Updating a username succeeds and returns the updated profile
    Given a user exists with id "00000000-0000-0000-0000-000000000001" and username "alice"
    When user "00000000-0000-0000-0000-000000000001" username is updated to "bob"
    Then the update result is success with username "bob"

  Scenario: Enabling dev_mode only succeeds and leaves the username unchanged
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" and dev_mode false
    When user "00000000-0000-0000-0000-000000000001" dev_mode is updated to true
    Then the update result is success with username "alice"
    And the update result has dev_mode true

  Scenario: Updating the username only leaves dev_mode unchanged
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" and dev_mode true
    When user "00000000-0000-0000-0000-000000000001" username is updated to "bob"
    Then the update result is success with username "bob"
    And the update result has dev_mode true

  Scenario: Updating username and dev_mode together succeeds
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" and dev_mode false
    When user "00000000-0000-0000-0000-000000000001" username is updated to "bob" and dev_mode to true
    Then the update result is success with username "bob"
    And the update result has dev_mode true

  Scenario: Updating with no fields fails
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" and dev_mode false
    When user "00000000-0000-0000-0000-000000000001" is updated with no fields
    Then the update result is invalid input "at least one of username or dev_mode is required"

  Scenario: Updating a user with a non-UUID id fails
    When user "not-a-uuid" username is updated to "bob"
    Then the update result is invalid input "user_id must be a valid UUID"

  Scenario: Updating a user with an empty username fails
    Given a user exists with id "00000000-0000-0000-0000-000000000001" and username "alice"
    When user "00000000-0000-0000-0000-000000000001" username is updated to ""
    Then the update result is invalid input "username is required"

  Scenario: Updating a user with a whitespace-only username fails
    Given a user exists with id "00000000-0000-0000-0000-000000000001" and username "alice"
    When user "00000000-0000-0000-0000-000000000001" username is updated to "   "
    Then the update result is invalid input "username is required"

  Scenario: Updating a user that does not exist fails
    When user "00000000-0000-0000-0000-000000000099" username is updated to "bob"
    Then the update result is not found

  Scenario: Updating to a username already taken fails
    Given a user exists with id "00000000-0000-0000-0000-000000000001" and username "alice"
    And the database signals a unique constraint violation on next save
    When user "00000000-0000-0000-0000-000000000001" username is updated to "bob"
    Then the update result is conflict
