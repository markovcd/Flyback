Feature: A Plot draws a pitch function as a curve across beats
  A song written as functions of beats is seen by drawing each function as a
  curve: across is beats, up is pitch, and a dot rides the curve at now. A Plot
  in the Picture plugin draws one, laid over the picture, so a song of several
  functions is several Plots chained together.

  Scenario: Each beat of the function is drawn at its pitch
    Given a Plot of "floor(t)" turning a page every 4 beats at 60 bpm
    Then 2 seconds in, beat 2.5 is drawn at pitch 2
    And 2 seconds in, nothing is drawn at beat 2.5 and pitch 1

  Scenario: A page shows what has played and keeps what is still to come dark
    Given a Plot of "2" turning a page every 4 beats at 60 bpm, with what is to come hidden
    Then 1 second in, beat 0.5 is drawn at pitch 2
    And 1 second in, nothing is drawn at beat 3 and pitch 2

  Scenario: The next page starts when the last one is full
    Given a Plot of "floor(t / 4)" turning a page every 4 beats at 60 bpm
    Then 5 seconds in, beat 7.5 is drawn at pitch 1
