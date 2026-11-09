playing-card-name-reverse = игральная карта
playing-card-desc-reverse = С этой стороны невозможно узнать, что это за карта.

playing-card-name = {$card} {$suit}
playing-card-desc = Изысканное оформление!

playing-card-suit-name = { $suit ->
    [clubs] треф
    [diamonds] бубен
    [hearts] червей
    [spades] пик
   *[invalid] !!{$suit}!!
}

playing-card-value-name = { $card ->
    [ace] Туз
    [J] Валет
    [Q] Дама
    [K] Король
   *[other] {$card}
}

playing-card-joker = Джокер
