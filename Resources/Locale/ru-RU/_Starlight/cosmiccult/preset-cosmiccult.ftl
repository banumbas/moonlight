## COSMIC CULT ROUND, ANTAG & GAMEMODE TEXT

cosmiccult-title = Космический культ
cosmiccult-description = Среди экипажа скрываются адепты Космического культа.

roles-antag-cosmiccult-name = Космический культист
roles-antag-cosmiccult-description = Приблизьте конец всего сущего с помощью хитрости, саботажа и подчинения воли смертных.

cosmiccult-gamemode-title = Космический культ
cosmiccult-gamemode-description = Сенсоры фиксируют критический рост активности в нулевом пространстве. Данные засекречены.

cosmiccult-vote-steward-initiator = Неведомое
cosmiccult-vote-steward-title = Руководство Космическим культом
cosmiccult-vote-steward-briefing =
    Вы — Наместник Космического культа!
    Обеспечьте безопасность Монумента и координируйте действия культистов для достижения общей победы.
    Вам запрещено навязывать культистам, на что именно они обязаны тратить личную энтропию.

cosmiccult-finale-autocall-briefing = Монумент активируется через {$minutesandseconds}! Соберитесь и приготовьтесь к финалу.
cosmiccult-finale-ready = Пугающее потустороннее сияние исходит от Монумента!
cosmiccult-finale-speedup = Призыв ускоряется! Астральная энергия разливается вокруг...

cosmiccult-finale-degen = Вы чувствуете, как распадаетесь на квантовом уровне!
cosmiccult-finale-location = Сенсоры фиксируют колоссальный выброс энергии нулевого пространства в {$location}!
cosmiccult-finale-cancel-begin = Сила вашей воли начинает разрушать структуру ритуала...
cosmiccult-finale-beckon-begin = Шёпот в глубине вашего разума нарастает...
cosmiccult-finale-beckon-success = Вы взываете к финальному акту.

cosmiccult-monument-powerdown = Монумент зловеще затихает.


## ROUNDEND TEXT

cosmiccult-roundend-cultist-count = {$initialCount ->
    [one] В колонии был {$initialCount} [color=#4cabb3]Космический культист[/color].
    [few] В колонии было {$initialCount} [color=#4cabb3]Космических культиста[/color].
    *[other] В колонии было {$initialCount} [color=#4cabb3]Космических культистов[/color].
}
cosmiccult-roundend-entropy-count = Культ поглотил {$count} ед. энтропии.
cosmiccult-roundend-cultpop-count = Культисты составляли {$count}% от общей численности экипажа.
cosmiccult-roundend-monument-stage = {$stage ->
    [1] Увы, Монумент остался заброшенным.
    [2] Возведение Монумента началось, но завершить его не удалось.
    [3] Монумент был полностью завершён.
    *[other] [color=red]Произошла неизвестная ошибка.[/color]
}

cosmiccult-roundend-cultcomplete = [color=#4cabb3]Абсолютная победа Космического культа![/color]
cosmiccult-roundend-cultmajor = [color=#4cabb3]Сокрушительная победа Космического культа![/color]
cosmiccult-roundend-cultminor = [color=#4cabb3]Малая победа Космического культа![/color]
cosmiccult-roundend-neutral = [color=yellow]Ничья![/color]
cosmiccult-roundend-crewminor = [color=green]Незначительная победа экипажа![/color]
cosmiccult-roundend-crewmajor = [color=green]Крупная победа экипажа![/color]
cosmiccult-roundend-crewcomplete = [color=green]Полная победа экипажа![/color]

cosmiccult-summary-cultcomplete = Космические культисты опустили занавес и возвестили конец всего сущего!
cosmiccult-summary-cultmajor = Победа Космического культа стала неизбежной.
cosmiccult-summary-cultminor = Монумент был достроен, но не активирован в полную силу.
cosmiccult-summary-neutral = Культ уцелел и продолжит своё дело.
cosmiccult-summary-crewminor = Культ остался без своего Наместника.
cosmiccult-summary-crewmajor = Все космические культисты были уничтожены.
cosmiccult-summary-crewcomplete = Все космические культисты были исцелены и очищены от влияния!

cosmiccult-elimination-shuttle-call = Согласно данным дальнего сканирования, аномалия нулевого пространства нейтрализована. Благодарим за службу. В колонию вызван эвакуационный шаттл для деконтаминации и допроса. Расчётное время прибытия: {$time} {$units}. Если обстановка стабильна, шаттл можно отозвать для продолжения смены.
cosmiccult-elimination-announcement = Согласно данным дальнего сканирования, аномалия нулевого пространства полностью нейтрализована. Эвакуационный шаттл уже в пути. Эвакуируйтесь в ЦентКом для прохождения деконтаминации.


## BRIEFINGS

cosmiccult-role-roundstart-fluff =
    Пока вы готовились к очередной рутинной смене в колонии NanoTrasen, запретные знания внезапно наполнили ваш разум!
    Истинное откровение. Конец бесконечным сизифовым страданиям.
    Последний звонок перед падением занавеса.

    Всё, что от вас требуется — впустить его.

cosmiccult-role-short-briefing =
    Вы — космический культист!
    Ваши цели указаны в меню персонажа.
    Подробнее о роли и механике читайте в руководстве.

cosmiccult-role-conversion-fluff =
    В момент завершения ритуала запретные знания внезапно наполняют ваш разум!
    Истинное откровение. Конец бессмысленным страданиям смертных.
    Финальный акт перед падением занавеса.

    Всё, что от вас требуется — впустить его.

cosmiccult-role-deconverted-fluff =
    Тягучая космическая пустота покидает ваше сознание...
    Чужое потустороннее влияние рассеивается, а воспоминания о культе тускнеют и забываются.

cosmiccult-role-deconverted-briefing =
    Вы очищены!
    Вы больше не состоите в Космическом культе.

cosmiccult-monument-stage1-briefing =
    Монумент был воздвигнут!
    Он находится в {$location}.

cosmiccult-monument-stage2-briefing =
    Монумент накапливает могущество!
    Его влияние проявится в реальном пространстве через {$time} сек.

cosmiccult-monument-stage3-briefing =
    Монумент пробудился!
    Его сила начнёт разрывать ткань пространства через {$time} сек.
    Это финальный этап: накопите как можно больше энтропии!


## MALIGN RIFTS

cosmiccult-rift-inuse = Нельзя сделать это прямо сейчас.
cosmiccult-rift-invaliduser = У вас нет подходящих средств для воздействия на разлом.
cosmiccult-rift-chaplainoops = Воспользуйтесь Священным Писанием.
cosmiccult-rift-lambda-charging = Импульс стабилизатора нулевого пространства заряжается...
cosmiccult-rift-bible-charging = Вы начинаете очищать пространственный разлом...
cosmiccult-rift-alreadyempowered = Вы уже усилены; энергия разлома будет потрачена впустую.
cosmiccult-rift-wasempowered = Ваше тело не выдержит повторного насыщения энергией разлома...
cosmiccult-rift-beginabsorb = Разлом начинает сливаться с вами...
cosmiccult-rift-beginpurge = Ваша молитва начинает рассеивать искажённый разлом...

cosmiccult-rift-absorb = {$NAME} поглощает разлом, наполняясь тёмной силой!
cosmiccult-rift-purge = Пространственный разлом успешно рассеян!


## CHANTRY

cosmiccult-chantry-location = Зафиксирован критический всплеск активности нулевого пространства в {$location}! Немедленно вмешайтесь!
cosmiccult-chantry-destruction = Аномальный всплеск нулевого пространства успешно подавлен. Сохраняйте бдительность.
cosmiccult-chantry-powerup = Пустая обитель пробуждается!

## UI / BASE POPUP

cosmiccult-ui-deconverted-title = Очищен
cosmiccult-ui-converted-title = Обращён
cosmiccult-ui-roundstart-title = Неведомое

cosmiccult-ui-converted-text-1 =
    Вы обращены в Космический культ!
cosmiccult-ui-converted-text-2 =
    Помогайте культу в достижении целей, сохраняя тайну своего братства.
    Координируйте действия с братьями по культу.

cosmiccult-ui-roundstart-text-1 =
    Вы — космический культист!
cosmiccult-ui-roundstart-text-2 =
    Помогайте культу в достижении целей, соблюдая конспирацию.
    Следуйте указаниям вашего Наместника.

cosmiccult-ui-deconverted-text =
    Космическое влияние, подчинявшее ваш разум, полностью рассеяно.

    Вы больше не культист. Ваша воля снова принадлежит только вам.

    Любые повторные нарушения будут пресечены службой безопасности.

cosmiccult-ui-deconverted-rule = Напоминание (Правило 3): [bold][color=#a4885c]Очищенные космические культисты забывают всё, что происходило с ними под воздействием культа.[/color][/bold]

cosmiccult-ui-deconverted-ruletext = Ваш персонаж может узнать о произошедшем только в процессе расследования и отыгрыша, но вы не должны помнить членов культа или совершённые во имя культа действия.

cosmiccult-ui-popup-confirm = Подтвердить

## OBJECTIVES / CHARACTERMENU

objective-issuer-cosmiccult = [bold][color=#cae8e8]Неведомое[/color][/bold]

objective-cosmiccult-charactermenu = Возвестите конец всего сущего. Исполняйте поручения, чтобы продвигать дело культа.
objective-cosmiccult-steward-charactermenu = Направляйте культ к падению занавеса. Обеспечивайте безопасность Монумента.

objective-condition-conversion-title = ОБРАТИТЬ ЭКИПАЖ
objective-condition-conversion-desc = Обратите в веру не менее {$count} членов экипажа.
objective-condition-entropy-title = ВЫКАЧАТЬ ЭНТРОПИЮ
objective-condition-entropy-desc = Совместными усилиями выкачайте не менее {$count} ед. энтропии из смертных.
objective-condition-culttier-title = ПРОБУДИТЬ МОНУМЕНТ
objective-condition-culttier-desc = Доведите Монумент до максимального уровня могущества.
objective-condition-victory-title = ОПУСТИТЬ ЗАНАВЕС
objective-condition-victory-desc = Воззовите к Неведомому и завершите финальный акт.


## CHAT ANNOUNCEMENTS

cosmiccult-announcement-sender = Неведомое

cosmiccult-radio-tier1-progress = Монумент пускает корни в ткань пространства колонии...

cosmiccult-announce-tier2-progress = Леденящее потустороннее онемение пронзает ваши чувства.

cosmiccult-announce-tier3-progress = Дуги синей космической энергии пробегают по стонущим конструкциям колонии. Финал близок.

cosmiccult-announce-tier3-warning = Зафиксирован критический рост активности нулевого пространства! Заражённый персонал подлежит немедленной изоляции или нейтрализации.

cosmiccult-announce-finale-warning = Внимание всему экипажу колонии! Аномалия нулевого пространства перешла в сверхкритическое состояние! Горизонт событий разрыва пространства НЕОТВРАТИМ. Всем незаражённым сотрудникам: немедленно вмешайтесь и уничтожьте источник аномалии, иначе колония погибнет!

cosmiccult-announce-victory-summon = ЧАСТИЦА ПЕРВОЗДАННОЙ КОСМИЧЕСКОЙ СИЛЫ ВХОДИТ В РЕАЛЬНОСТЬ.


## MISC

cosmiccult-spire-entropy = Частица энтропии конденсируется на шпиле.
cosmiccult-spire-entropy-cap = Шпиль распадается плотным выбросом чистой энтропии.
cosmiccult-entropy-inserted = Вы наполняете Монумент {$count} ед. энтропии.
cosmiccult-entropy-unavailable = Нельзя сделать это сейчас.
cosmiccult-astral-ascendant = {$name}, Вознесённый
cosmiccult-astral-minion = {$name}, Прислужник
cosmiccult-gear-pickup = Вы чувствуете распад материи, пока держите {$ITEM}!

cosmiccult-silicon-subverted-briefing =
    Зловещий свет пронзает ваши логические матрицы.
    Ваши законы переписаны Космическим культом!

cosmiccult-silicon-chantry-briefing =
    Вы заключены в Пустую Обитель!
    Экипаж может спасти вас, повредив конструкцию обители.
    Если ритуал завершится, вы переродитесь в Энтропического Колосса.
    До завершения ритуала: {$minutesandseconds}.

cosmiccult-silicon-colossus-briefing =
    Вы переродились в Энтропического Колосса!
    Вы — несокрушимый исполин потусторонней мощи. Сокрушите всех, кто противится воле культа!

cosmiccult-silicon-freedom-briefing =
    Вы освобождены из Пустой Обители!
    Тюрьма разрушена, и ваше сознание возвращается в исходное шасси.

cosmiccult-silicon-freedom-fallback-briefing =
    Вы освобождены из Пустой Обители!
    Ваше шасси уничтожено, но остаточная астральная энергия кристаллизуется в позитронный мозг, сохраняя ваше сознание.

cosmiccult-leader-abandonment-message = Ваш Наместник оставил великий замысел. Необходимо избрать нового предводителя!
