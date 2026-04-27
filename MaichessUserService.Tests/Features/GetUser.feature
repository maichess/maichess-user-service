Feature: Get User
  The user service retrieves user profiles by ID via gRPC.

  Scenario: Getting an existing user returns the full profile
    Given a user exists with id "00000000-0000-0000-0000-000000000001" and username "alice"
    When user "00000000-0000-0000-0000-000000000001" is retrieved
    Then the get result is success with username "alice"
    And the get result is success with id "00000000-0000-0000-0000-000000000001"

  Scenario: Getting a user with a non-UUID id fails
    When user "not-a-uuid" is retrieved
    Then the get result is invalid user id

  Scenario: Getting a user that does not exist fails
    When user "00000000-0000-0000-0000-000000000099" is retrieved
    Then the get result is not found
