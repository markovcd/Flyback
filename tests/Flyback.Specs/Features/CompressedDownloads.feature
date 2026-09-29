Feature: The preset site sends the web viewer and the web editor compressed
  The runtime a browser fetches to play or edit a patch is tens of megabytes as it
  lies. Its build writes a compressed copy of each file beside it, and a browser that
  takes one is sent that, kept as long as the file itself.

  Scenario: A browser that takes gzip gets the web viewer's runtime as its gzip copy
    When a browser that takes "gzip" fetches the web viewer's runtime
    Then it is sent the "gzip" copy, which unpacks to the file itself
    And the runtime is kept for good

  Scenario: A browser that takes no compression gets the web viewer's runtime as it lies
    When a browser that takes "identity" fetches the web viewer's runtime
    Then it is sent the file as it lies

  Scenario: The web editor's runtime is sent as its brotli copy and kept for good
    Given a web editor beside the preset site
    When a browser that takes "br, gzip" fetches the web editor's runtime
    Then it is sent the "br" copy, which unpacks to the file itself
    And the runtime is kept for good
    And the web editor's loader is checked with the site before each use
