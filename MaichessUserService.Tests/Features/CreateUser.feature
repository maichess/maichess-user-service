Feature: Create User
  The user service creates user accounts via gRPC on behalf of the Auth service.

  Scenario: Creating a user with valid credentials returns the new profile
    When a user is created with username "alice" and password hash "hash123"
    Then the create result is success with username "alice"
    And the created user has Elo 1200
    And the created user has zero wins losses and draws
    And the created user has dev_mode false
    And the database insert stored password hash "hash123" under the "password_hash" field
    And the database insert stored dev_mode false under the "dev_mode" field

  Scenario: Creating a user with an empty username fails
    When a user is created with username "" and password hash "hash123"
    Then the create result is invalid input "username is required"

  Scenario: Creating a user with a whitespace-only username fails
    When a user is created with username "   " and password hash "hash123"
    Then the create result is invalid input "username is required"

  Scenario: Creating a user with an empty password hash fails
    When a user is created with username "alice" and password hash ""
    Then the create result is invalid input "password_hash is required"

  Scenario: Creating a user with a username already taken fails
    Given the database signals a unique constraint violation on next save
    When a user is created with username "alice" and password hash "hash123"
    Then the create result is conflict
