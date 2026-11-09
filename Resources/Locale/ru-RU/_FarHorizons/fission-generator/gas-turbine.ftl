### Examine

gas-turbine-examine-stator-null = Кажется, отсутствует статор.
gas-turbine-examine-stator = Статор установлен.

gas-turbine-examine-blade-null = Кажется, отсутствует турбинная лопатка.
gas-turbine-examine-blade = Турбинная лопатка установлена.

gas-turbine-spinning-0 = Лопасти неподвижны.
gas-turbine-spinning-1 = Лопасти медленно вращаются.
gas-turbine-spinning-2 = Лопасти вращаются.
gas-turbine-spinning-3 = Лопасти быстро вращаются.
gas-turbine-spinning-4 = [color=red]Лопасти вращаются на запредельной скорости![/color]

gas-turbine-damaged-0 = Внешне турбина в отличном состоянии.
gas-turbine-damaged-1 = Турбина выглядит слегка потёртой.
gas-turbine-damaged-2 = [color=yellow]Турбина имеет заметные повреждения.[/color]
gas-turbine-damaged-3 = [color=orange]Турбина сильно повреждена![/color]

gas-turbine-ruined = [color=red]Турбина полностью разрушена![/color]

### Popups

# Shown when an event occurs
gas-turbine-overheat = На {$owner} срабатывает клапан аварийного сброса давления при перегреве!
gas-turbine-explode = {CAPITALIZE(THE($owner))} разрывает на куски от перегрузки!

# Shown when damage occurs
gas-turbine-spark = {CAPITALIZE(THE($owner))} начинает искрить!
gas-turbine-spark-stop = {CAPITALIZE(THE($owner))} перестаёт искрить.
gas-turbine-smoke = {CAPITALIZE(THE($owner))} начинает дымить!
gas-turbine-smoke-stop = {CAPITALIZE(THE($owner))} перестаёт дымить.

# Shown during repairs
gas-turbine-repair-fail-blade = Сначала необходимо заменить турбинную лопатку.
gas-turbine-repair-fail-stator = Сначала необходимо заменить статор.
gas-turbine-repair-ruined = Вы восстанавливаете основные узлы {THE($target)} с помощью {THE($tool)}.
gas-turbine-repair-partial = Вы устраняете часть повреждений {THE($target)} с помощью {THE($tool)}.
gas-turbine-repair-complete = Вы полностью завершаете ремонт {THE($target)} с помощью {THE($tool)}.
gas-turbine-repair-no-damage = {CAPITALIZE(THE($target))} не имеет повреждений, которые можно было бы устранить с помощью {THE($tool)}.

# Anchoring warnings
gas-turbine-unanchor-warning = Вы не можете открутить {THE($owner)}, пока турбина вращается!
gas-turbine-anchor-warning = Неподходящее место для крепления.

gas-turbine-eject-fail-speed = Нельзя извлекать детали турбины на ходу!
gas-turbine-insert-fail-speed = Нельзя устанавливать детали турбины на ходу!

### UI

# Shown when using the UI
gas-turbine-ui-tab-main = Управление
gas-turbine-ui-tab-parts = Детали

gas-turbine-ui-rpm = Об/мин

gas-turbine-ui-overspeed = ПЕРЕГРУЗКА
gas-turbine-ui-overtemp = ПЕРЕГРЕВ
gas-turbine-ui-stalling = СРЫВ ПОТОКА
gas-turbine-ui-undertemp = НИЗКАЯ ТЕМПЕРАТУРА

gas-turbine-ui-flow-rate = Скорость потока
gas-turbine-ui-stator-load = Нагрузка статора

gas-turbine-ui-blade = Турбинная лопатка
gas-turbine-ui-blade-integrity = Прочность
gas-turbine-ui-blade-stress = Нагрузка

gas-turbine-ui-stator = Статор турбины
gas-turbine-ui-stator-potential = Потенциал
gas-turbine-ui-stator-supply = Выработка

gas-turbine-ui-power = { POWERWATTS($power) }

gas-turbine-ui-locked-message = Панель управления заблокирована.
gas-turbine-ui-footer-left = ОПАСНОСТЬ: ВЫСОКОСКОРОСТНЫЕ ВРАЩАЮЩИЕСЯ ЭЛЕМЕНТЫ
gas-turbine-ui-footer-right = 2.1 РЕД. 1
