Feature: Get User
  The user service retrieves user profiles by ID via gRPC.

  Scenario: Getting an existing user returns the full profile
    Given a user exists with id "00000000-0000-0000-0000-000000000001" and username "alice"
    When user "00000000-0000-0000-0000-000000000001" is retrieved
    Then the get result is success with username "alice"
    And the get result is success with id "00000000-0000-0000-0000-000000000001"

  Scenario: Getting a user returns their dev_mode flag
    Given a user exists with id "00000000-0000-0000-0000-000000000001" username "alice" and dev_mode true
    When user "00000000-0000-0000-0000-000000000001" is retrieved
    Then the get result has dev_mode true

  Scenario: Getting a legacy user without a dev_mode field defaults to false
    Given a legacy user exists with id "00000000-0000-0000-0000-000000000002" and username "carol" with no dev_mode field
    When user "00000000-0000-0000-0000-000000000002" is retrieved
    Then the get result has dev_mode false

  Scenario: Getting a user with a non-UUID id fails
    When user "not-a-uuid" is retrieved
    Then the get result is invalid user id

  Scenario: Getting a user that does not exist fails
    When user "00000000-0000-0000-0000-000000000099" is retrieved
    Then the get result is not found
