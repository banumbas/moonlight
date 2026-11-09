## Base actions

alerts-vampire-blood-name = Выпито крови
alerts-vampire-blood-desc = Количество выпитой вами крови. Обнажите клыки и нажмите ЛКМ по жертве, чтобы пить её кровь.

alerts-vampire-fed-name = Сытость
alerts-vampire-fed-desc = Ваш текущий уровень насыщения кровью. Пейте кровь, чтобы поддерживать силы.

roles-antag-vamire-name = Вампир
roles-antag-vampire-description = Охотьтесь на экипаж. Обнажите клыки и пейте их кровь.

roles-antag-thrall-name = Тралл
roles-antag-thrall-objective = Преданно служите своему господину и беспрекословно исполняйте его приказы.

vampire-roundend-name = вампир

vampire-drink-start = Вы вонзаете клыки в {CAPITALIZE(THE($target))}.

vampire-not-enough-blood = Недостаточно крови.

vampire-mouth-covered = Ваш рот закрыт!
vampire-drink-invalid-target = Вы не можете пить кровь вампиров или их рабов.
vampire-target-protected-by-faith = Это существо защищено святой верой!
vampire-drink-target-empty = В этом существе не осталось крови!
vampire-drink-target-maxed = Вы уже выпили максимум крови ({ $amount } ед.) из этой жертвы.
vampire-drink-target-hard-max = Вы иссушили эту цель до предела ({ $amount } ед.).
vampire-full-power-achieved = Ваша вампирическая сила достигла истинного апогея!
vampire-umbrae-full-power-fov = Тени полностью подчиняются вам. Теперь вы способны видеть сквозь стены!
vampire-drink-target-not-viable = У этого существа не бьётся сердце!
vampire-drink-target-rot = Плоть этого существа уже сгнила!
vampire-sleep-shielded = Защитный имплант цели блокирует погружение в сон!
vampire-sleep-protected = Необходим прямой зрительный контакт...

vampire-role-greeting = Вы — вампир!
    Неутолимая жажда заставляет вас охотиться на членов экипажа. Используйте тёмные силы, чтобы уничтожить или подчинить колонию.
    Ваши клыки позволяют пить кровь гуманоидов — это восстанавливает здоровье и открывает могущественные способности.
    Устройте кровавую жатву в эту смену!

# Objectives
objective-issuer-vampire = [color=crimson]Вампир[/color]

objective-condition-drain-title = Выпить {$count} ед. крови
objective-condition-drain-description = Выпейте {$count} единиц крови у членов экипажа своими клыками.

objective-vampire-thrall-obey-master-title = Повинуйтесь своему господину, {$targetName}.

# Class selection action
action-vampire-class-select = Выбрать подкласс вампира
action-vampire-class-select-desc = Выберите свой вампирический путь и способности

# Round end statistics
roundend-prepend-vampire-drained-low = Вампиры практически голодали в эту смену, выпив всего {$blood} ед. крови.
roundend-prepend-vampire-drained-medium = Вампиры неплохо подкрепились, поглотив {$blood} ед. крови.
roundend-prepend-vampire-drained-high = Вампиры устроили настоящий кровавый пир, поглотив {$blood} ед. крови!
roundend-prepend-vampire-drained-critical = Вампиры устроили безумную резню, выпив невероятные {$blood} ед. крови!

roundend-prepend-vampire-drained = Ни одному вампиру не удалось собрать сколь-нибудь значительное количество крови в этом раунде.
roundend-prepend-vampire-drained-named = Самым кровожадным вампиром оказался {$name}, выпивший {$number} ед. крови.

# Vampire class selection tooltips
vampire-class-hemomancer-tooltip = Гемомант
    Специализируется на магии крови, кровавых шипах и управлении жизненной силой вокруг себя.

vampire-class-umbrae-tooltip = Умбра
    Мастер теней, скрытных нападений, ловушек и мгновенных перемещений.

vampire-class-gargantua-tooltip = Гаргантюа
    Воплощение грубой физической силы, разрушительного ближнего боя и невероятной стойкости.

vampire-class-dantalion-tooltip = Данталион
    Владыка разума: специализируется на порабощении смертных, иллюзиях и контроле слуг.

# Hemomancer abilities
action-vampire-hemomancer-tendrils-wrong-place = Нельзя применить способность в эту точку.

action-vampire-blood-barrier-wrong-place = Здесь нельзя возвести кровавый барьер.

action-vampire-sanguine-pool-already-in = Вы уже находитесь в форме лужи крови!
action-vampire-sanguine-pool-invalid-tile = Здесь невозможно растечься лужей крови.
action-vampire-sanguine-pool-enter = Вы превращаетесь в лужу крови!
action-vampire-sanguine-pool-exit = Вы восстанавливаете гуманоидную форму из лужи крови!
vampire-space-burn-warning = Безжалостное космическое излучение обжигает вашу мёртвую плоть!

action-vampire-blood-eruption-activated = Вы заставляете кровь вокруг взорваться смертоносными шипами!

action-vampire-blood-bringers-rite-not-enough-power = Вам не хватает вампирического могущества (требуется более 1000 ед. выпитой крови и 8 уникальных жертв).
action-vampire-blood-brighters-rite-not-enough-blood = Недостаточно крови для проведения ритуала Несущих Кровь.
action-vampire-blood-bringers-rite-start = Ритуал Несущих Кровь начат!
action-vampire-blood-bringers-rite-stop = Ритуал Несущих Кровь прекращён.
action-vampire-blood-bringers-rite-stop-blood = Ритуал Несущих Кровь прерван: закончилась кровь.

vampire-locate-result = Ваше чутьё указывает путь от { $target } к { $location }.
vampire-locate-not-same-sector = Эта жертва находится вне вашего сектора.
vampire-locate-unknown = Неизвестная область
vampire-locate-no-targets = В этом секторе не ощущается подходящих жертв.

predator-sense-title = Чутьё хищника
vampire-locate-search-placeholder = Поиск...

vampiric-claws-remove-popup = Вы убираете вампирические когти.

# Umbrae abilities
action-vampire-cloak-of-darkness-start = Вы растворяетесь во тьме!
action-vampire-cloak-of-darkness-stop = Вы выходите из тени.

action-vampire-shadow-snare-placed = Вы установили теневую ловушку.
action-vampire-shadow-snare-wrong-place = Здесь нельзя поставить ловушку.
action-vampire-shadow-snare-scatter = Теневая ловушка развеяна.
vampire-shadow-snare-oldest-removed = Ваша предыдущая теневая ловушка рассеивается.
ent-shadow-snare-ensnare = теневая ловушка

action-vampire-shadow-anchor-returned = Вы возвращаетесь к теневому якорю!
action-vampire-shadow-anchor-installed = Вы закрепили теневой якорь в этом месте.

action-vampire-shadow-boxing-start = Вы начинаете бой с тенью.
action-vampire-shadow-boxing-stop = Бой с тенью прекращён.
action-vampire-shadow-boxing-ends = Бой с тенью завершён.

action-vampire-dark-passage-wrong-place = Здесь недостаточно темно...
action-vampire-dark-passage-activated = Вы мгновенно проскальзываете сквозь тьму...

action-vampire-extinguish-activated = Вы поглощаете свет вокруг себя... ({$count})

action-vampire-eternal-darkness-not-enough-blood = У вас закончилась кровь для поддержания вечной тьмы!
action-vampire-eternal-darkness-start = Вы погружаете окружение в вечную тьму...
action-vampire-eternal-darkness-stop = Вечная тьма рассеивается...

# Dantalion
vampire-enthrall-start = Вы проникаете в сознание {CAPITALIZE(THE($target))}...
vampire-enthrall-success = {CAPITALIZE(THE($target))} склоняется перед вами, становясь преданным рабом!
vampire-enthrall-target = Ваш разум полностью подчиняет вампирическая воля!
vampire-enthrall-limit = Вы достигли максимального количества подчинённых рабов.
vampire-enthrall-invalid = Эту цель невозможно поработить.
vampire-thrall-released = Вампирический контроль над вашим разумом спадает.

vampire-pacify-invalid = Эту цель невозможно усмирить.
vampire-pacify-success = {CAPITALIZE(THE($target))} поддаётся неестественному гипнотическому спокойствию.
vampire-pacify-target = Подавляющее спокойствие лишает вас воли к сражению!

vampire-subspace-swap-thrall = Вы не можете меняться местами со своими рабами.
vampire-subspace-swap-dead = Разум этой цели недосягаем.
vampire-subspace-swap-failed = Пространственный разлом с шипением захлопывается.
vampire-subspace-swap-success = Пространство искажается, и вы меняетесь местами с {CAPITALIZE(THE($target))}!
vampire-subspace-swap-target = Реальность искажается, и вас резко переносит на новое место!

vampire-rally-thralls-success = {$count ->
    [one] Ваш зов призывает раба к вам!
    [few] Ваш зов призывает {$count} рабов к вам!
    *[other] Ваш зов призывает {$count} рабов к вам!
}
vampire-rally-thralls-none = Ни один из ваших рабов не в состоянии откликнуться на зов.
vampire-thrall-holy-water-freed = Святая вода очищает ваш разум от вампирского внушения!

vampire-blood-bond-start = Узы крови связывают вас с вашими рабами.
vampire-blood-bond-stop = Вы разрываете узы крови.
vampire-blood-bond-no-thralls = У вас нет порабощённых слуг для создания связи.
vampire-blood-bond-stop-blood = Связь разрывается: вам не хватает крови для её поддержания.

action-vampire-not-enough-power = Вашей силы недостаточно (требуется более 1000 ед. крови и 8 уникальных жертв).

# Gargantua
vampire-blood-swell-start = Ваши мускулы раздуваются от нечестивой мощи!
vampire-blood-swell-end = Кровавая ярость утихает.

vampire-blood-rush-start = Кровь с бешеной силой устремляется по жилам!
vampire-blood-rush-end = Сверхъестественная скорость спадает.

vampire-seismic-stomp-activate = Земля содрогается от вашей ярости!

vampire-overwhelming-force-start = Ваша хватка становится несокрушимой.
vampire-overwhelming-force-stop = Вы ослабляете железную хватку.
vampire-overwhelming-force-too-heavy = Этот объект слишком тяжёл даже для вашей мощи!
vampire-overwhelming-force-door-pried = Вы выламываете створки шлюза нечеловеческой силой!

vampire-demonic-grasp-hit = Демонический коготь впивается в вас!
vampire-demonic-grasp-pull = Теневой коготь притягивает вас прямо к вампиру!

vampire-charge-start = Вы совершаете сокрушительный рывок вперёд!
vampire-charge-impact = Вы на полной скорости врезаетесь в {CAPITALIZE(THE($target))}!

vampire-blood-swell-cancel-shoot = Ваши пальцы слишком огромны и не помещаются в спусковую скобу!

vampire-holy-place-burn = Святая земля невыносимо обжигает вашу осквернённую плоть!

alerts-vampire-blood-swell-name = Наливание мышц
alerts-vampire-blood-swell-desc = Ваши мышцы наполнены нечестивой мощью.
alerts-vampire-blood-rush-name = Прилив крови
alerts-vampire-blood-rush-desc = Сверхъестественная скорость наполняет ваше тело.

Vamp-converted-title = Порабощён!
Vamp-converted-text =
    Ваш разум порабощён вампиром!
    Беспрекословно повинуйтесь своему господину. Вы можете общаться в разуме слуг с помощью команды «+p».
Vamp-converted-confirm = Слушаюсь
