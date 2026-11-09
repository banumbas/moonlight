## Secure Command Terminal – UI strings

secure-terminal-window-title = Защищённый командный терминал
secure-terminal-requests-header = Запросы
secure-terminal-information-header = Информация
secure-terminal-authorization-header = Авторизация

secure-terminal-select-request = Выберите запрос из списка слева для просмотра информации.

secure-terminal-request-button = Запросить
secure-terminal-request-button-confirm = Подтвердить запрос?
secure-terminal-authorize-button = Авторизовать
secure-terminal-deny-button = Отклонить
secure-terminal-recall-button = Отозвать арсенал
secure-terminal-recall-locked = { $minutes ->
    [one] Возврат возможен через {$minutes} минуту.
    [few] Возврат возможен через {$minutes} минуты.
    *[other] Возврат возможен через {$minutes} минут.
}
secure-terminal-used-note = Этот арсенал уже был активирован или отозван в этой смене и не может быть развёрнут повторно.
secure-terminal-already-used = Данный ресурс уже использовался в этой смене и не может быть запрошен повторно.

secure-terminal-auth-waiting = По этому запросу нет активных предложений.
secure-terminal-auth-desc = Статус подтверждения (ожидание = [color=red]красный[/color], подтверждено = [color=green]зелёный[/color]):
secure-terminal-awaiting-member = Ожидается подпись: {$label}

secure-terminal-pending-countdown-label = Истекает через {$minutes}м {$seconds}с…
secure-terminal-countdown-label = Активация через {$minutes}м {$seconds}с…

secure-terminal-fee-note = Комиссия за обработку: {$fee}
secure-terminal-salary-note = Зарплата экипажа снижена на {$penalty}% в связи с расходами на чрезвычайную мобилизацию.
secure-terminal-delay-note = { $minutes ->
    [one] Расчётное время прибытия: {$minutes} минута после авторизации.
    [few] Расчётное время прибытия: {$minutes} минуты после авторизации.
    *[other] Расчётное время прибытия: {$minutes} минут после авторизации.
}

secure-terminal-requires-no-war-note = Недоступно во время военного положения.
secure-terminal-requires-war-note = Доступно исключительно во время объявленных военных действий.
secure-terminal-requires-alert-note = Требуется активный код тревоги: {$level}.
secure-terminal-alert-time-remaining = { $minutes ->
    [one] Код тревоги должен действовать ещё {$minutes} минуту перед отправкой запроса.
    [few] Код тревоги должен действовать ещё {$minutes} минуты перед отправкой запроса.
    *[other] Код тревоги должен действовать ещё {$minutes} минут перед отправкой запроса.
}
secure-terminal-on-cooldown-note = { $minutes ->
    [one] Перезарядка — запрос будет доступен через {$minutes} минуту.
    [few] Перезарядка — запрос будет доступен через {$minutes} минуты.
    *[other] Перезарядка — запрос будет доступен через {$minutes} минут.
}
secure-terminal-requires-alert-suffix = Требуется: {$level}
secure-terminal-requires-war-suffix = Требуется: военное положение

secure-terminal-reason = Укажите причину запроса:

## Server → global announcements

secure-terminal-proposal-created = Запрос «{$request}» сформирован и ожидает подтверждения Командования.
secure-terminal-proposal-created-reason = Запрос «{$request}» сформирован и ожидает подтверждения Командования. Причина: {$reason}
secure-terminal-proposal-denied = Запрос «{$request}» был отменён.
secure-terminal-proposal-denied-cc = Запрос «{$request}» отклонён Центральным Командованием.
secure-terminal-radio-proposal = Сформирован запрос «{$request}». Просьба уполномоченным лицам подойти к терминалу авторизации для утверждения или отклонения.
secure-terminal-radio-proposal-reason = Сформирован запрос «{$request}». Просьба уполномоченным лицам подойти к терминалу авторизации. Причина: {$reason}
secure-terminal-radio-denied = Запрос «{$request}» был отменён.
secure-terminal-activation-countdown = Запрос «{$request}» полностью утверждён.
    Прибытие через {$minutes} мин.
    Зарплатный фонд колонии скорректирован в связи с расходами на мобилизацию.
secure-terminal-unknown-job = Неизвестно

## Popup messages

secure-terminal-no-station = Терминал не привязан к колонии.
secure-terminal-request-denied = Доступ запрещён.
secure-terminal-authorize-denied = У вас нет полномочий для подтверждения этого запроса.
secure-terminal-requires-war = Запрос доступен только при официально объявленном военном положении.
secure-terminal-wrong-alert = Текущий уровень тревоги не соответствует условиям запроса.
secure-terminal-alert-not-long-enough = Уровень тревоги действует недостаточно долго для разблокировки этого запроса.
secure-terminal-recall-too-soon = Арсенал развёрнут слишком недавно для возможности отзыва.
secure-terminal-on-cooldown = Запрос находится на перезарядке.
secure-terminal-already-pending = Предложение по данному запросу уже находится на рассмотрении.
secure-terminal-already-active = Другой запрос уже обрабатывается. Дождитесь завершения текущего запроса.
secure-terminal-no-active-proposal = Активных предложений не обнаружено.
secure-terminal-already-authorized = Вы уже подтвердили это предложение.
secure-terminal-already-activated = Этот терминал уже утвердил данное предложение.
secure-terminal-auth-note = Этот терминал предназначен исключительно для авторизации.
secure-terminal-authorized-by = Внимание: запрос «{$request}» утверждён. Подписали: {$signatories}.
secure-terminal-armory-recalled = «{$request}»: издан приказ об отзыве. Развёртывание арсенала отменено.
secure-terminal-awaiting-admin = Внимание: запрос «{$request}» отправлен. Ожидается одобрение Центрального Командования.
secure-terminal-admin = Запрос одобрения ЦентКома: {$request}
    Причина: {$reason}
    Используйте консоль администратора для подтверждения/отклонения запроса.

## Request names & descriptions

secure-terminal-warops-security-name = ОБР (Особый отдел СБ)
secure-terminal-warops-security-desc = Развёртывает ударную группу ОБР СБ для отражения полномасштабной военной агрессии. Доступно только во время военных действий.
    Рекомендуется при прямом штурме колонии превосходящими силами противника.
secure-terminal-warops-security-announcement = Отряд быстрого реагирования (Особый отдел СБ) авторизован и направлен к колонии. Расчётное время прибытия: 30 минут.

secure-terminal-ert-security-name = ОБР (Служба безопасности)
secure-terminal-ert-security-desc = Развёртывает тактическое подразделение охраны ОБР.
secure-terminal-ert-security-announcement = Отряд быстрого реагирования (Служба безопасности) авторизован и направлен к колонии. Расчётное время прибытия: 10 минут.

secure-terminal-ert-engineering-name = ОБР (Инженерия)
secure-terminal-ert-engineering-desc = Развёртывает инженерный отряд ОБР для восстановления критической инфраструктуры колонии.
    Рекомендуется при катастрофических разрушениях корпуса, энергосети или атмосферного контура колонии.
secure-terminal-ert-engineering-announcement = Отряд быстрого реагирования (Инженерная служба) авторизован и направлен к колонии. Расчётное время прибытия: 10 минут.

secure-terminal-ert-medical-name = ОБР (Медблок)
secure-terminal-ert-medical-desc = Развёртывает медицинский отряд ОБР для массовой сортировки раненых и неотложной хирургии.
    Рекомендуется при массовых потерях или уничтожении медицинского отсека колонии.
secure-terminal-ert-medical-announcement = Отряд быстрого реагирования (Медицинская служба) авторизован и направлен к колонии. Расчётное время прибытия: 10 минут.

secure-terminal-ert-janitorial-name = ОБР (Утилизация и дезинфекция)
secure-terminal-ert-janitorial-desc = Развёртывает санитарное подразделение ОБР для ликвидации опасных загрязнений и восстановления порядка.
    Рекомендуется при масштабном биологическом или химическом заражении отсеков колонии.
secure-terminal-ert-janitorial-announcement = Отряд быстрого реагирования (Санитарно-дезинфекционная служба) авторизован и направлен к колонии. Расчётное время прибытия: 10 минут.

secure-terminal-ert-chaplain-name = ОБР (Капеллан)
secure-terminal-ert-chaplain-desc = Направляет капеллана ОБР для духовного окормления, поднятия морального духа экипажа и проведения погребальных обрядов.
secure-terminal-ert-chaplain-announcement = Отряд быстрого реагирования (Капеллан) авторизован и направлен к колонии. Расчётное время прибытия: 10 минут.

secure-terminal-ert-cburn-name = ОБР (Подразделение CBURN / РХБЗ)
secure-terminal-ert-cburn-desc = Развёртывает специализированный отряд радиационной, химической и биологической защиты (CBURN).
secure-terminal-ert-cburn-announcement = Отряд быстрого реагирования (Подразделение CBURN) авторизован и направлен к колонии. Расчётное время прибытия: 15 минут.

secure-terminal-code-gamma-name = Код ГАММА
secure-terminal-code-gamma-desc = Вводит в колонии уровень тревоги [color=palevioletred]ГАММА[/color]. Военное положение — все гражданские лица эвакуируются службой безопасности в защищённые сектора.
secure-terminal-code-gamma-announcement = Внимание! В колонии объявлен код ГАММА. Вводится военное положение. Всему гражданскому экипажу немедленно явиться к главам отделов для эвакуации в убежища.
secure-terminal-end-gamma-name = Отменить код ГАММА
secure-terminal-end-gamma-desc = Снимает код тревоги [color=palevioletred]ГАММА[/color] и возвращает колонию к зелёному коду. Требуется, чтобы код ГАММА действовал не менее 15 минут.
secure-terminal-end-gamma-announcement = Код ГАММА отменён. Колония возвращается к штатному режиму работы. Ожидайте распоряжений руководства.

secure-terminal-code-psi-name = Код ПСИ
secure-terminal-code-psi-desc = Вводит уровень тревоги [color=mediumpurple]ПСИ[/color]. Обнаружен бунт или заражение синтетиков — держитесь в группах и избегайте неавторизованных киборгов.
secure-terminal-code-psi-announcement = Внимание! Командование объявило код ПСИ. Зафиксирована враждебная активность кремниевых форм жизни. Экипажу собраться в безопасных зонах.
secure-terminal-end-psi-name = Отменить код ПСИ
secure-terminal-end-psi-desc = Снимает тревогу [color=mediumpurple]ПСИ[/color] и возвращает колонию к зелёному коду. Требуется, чтобы код ПСИ действовал не менее 15 минут.
secure-terminal-end-psi-announcement = Код ПСИ отменён. Синтетическая угроза нейтрализована. Колония возвращается к штатному режиму работы.

secure-terminal-armory-gamma-name = Арсенал ГАММА
secure-terminal-armory-gamma-desc = Запрашивает дроппод с [color=palevioletred]Гамма-арсеналом[/color] — тяжёлым вооружением для критических ситуаций. Одноразовый запрос.
secure-terminal-armory-gamma-announcement = Дроппод с Гамма-Арсеналом авторизован и направлен к колонии.

secure-terminal-armory-psi-name = Арсенал ПСИ
secure-terminal-armory-psi-desc = Запрашивает дроппод с [color=mediumpurple]Пси-арсеналом[/color] — электромагнитным и антикибернетическим оружием. Одноразовый запрос.
secure-terminal-armory-psi-announcement = Дроппод с Пси-Арсеналом авторизован и направлен к колонии.

secure-terminal-med-pod-name = Модуль неотложной медицинской помощи
secure-terminal-med-pod-desc = Сбрасывает капсулу скорой помощи с реанимационным и хирургическим комплексом.
secure-terminal-med-pod-announcement = Капсула неотложной медицинской помощи авторизована и направлена к колонии. Расчётное время прибытия: 5 минут.

secure-terminal-nukerequest-name = Коды самоуничтожения
secure-terminal-nukerequest-desc = Запрос кодов активации ядерного механизма самоуничтожения колонии.
    Злоупотребление запросом кодов строго карается. Запрос не гарантирует одобрения ЦентКомом.

secure-terminal-code-violet-name = Код ФИОЛЕТОВЫЙ
secure-terminal-code-violet-desc = Вводит уровень тревоги [color=Violet]ФИОЛЕТОВЫЙ[/color].

secure-terminal-end-violet-name = Отменить Фиолетовый код
secure-terminal-end-violet-desc = Снимает тревогу [color=Violet]ФИОЛЕТОВЫЙ[/color] и возвращает колонию к зелёному коду. Требуется, чтобы код действовал не менее 10 минут.

secure-terminal-emergency-maintenance-name = Аварийный доступ в техотсеки
secure-terminal-emergency-maintenance-desc = Разблокировать все шлюзы технических туннелей.
secure-terminal-emergency-maintenance-announcement = Ограничения доступа к техническим тоннелям и внешним шлюзам временно сняты.

secure-terminal-end-emergency-maintenance-name = Заблокировать доступ в техотсеки
secure-terminal-end-emergency-maintenance-desc = Восстановить стандартные допуски в техтуннели.
secure-terminal-end-emergency-maintenance-announcement = Стандартные ограничения доступа к техническим отсекам восстановлены.

secure-terminal-emergency-station-name = Аварийный доступ в всей колонии
secure-terminal-emergency-station-desc = Открыть свободный доступ через все шлюзы колонии.
secure-terminal-emergency-station-announcement = Ограничения доступа ко всем шлюзам колонии сняты в связи с чрезвычайной ситуацией. Правила проникновения в закрытые зоны продолжают действовать.

secure-terminal-end-emergency-station-name = Восстановить допуски в всей колонии
secure-terminal-end-emergency-station-desc = Восстановить стандартные ограничения доступа в всей колонии.
secure-terminal-end-emergency-station-announcement = Стандартные уровни доступа ко всем шлюзам колонии восстановлены.
