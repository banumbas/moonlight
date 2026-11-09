ban-panel-tabs-punishments = Прочие Наказания
ban-panel-punishment = Прочие Наказания
ban-panel-punishment-channels-label = Мут каналов
ban-panel-punishment-channel-local = Локальный
ban-panel-punishment-channel-whisper = Шепот
ban-panel-punishment-channel-radio = Радио
ban-panel-punishment-channel-looc = LOOC
ban-panel-punishment-channel-ooc = OOC
ban-panel-punishment-channel-emotes = Эмоции
ban-panel-punishment-channel-dead = Мертвые

ban-panel-punishment-other-label = Остальное
ban-panel-punishment-paper = Мут бумаги
ban-panel-punishment-pacifism = Пацифизм

punishment-examine-muted-channels = [color=red]Имеет мут на каналы: {$channels}[/color]
punishment-examine-paper-muted = [color=red]Не может писать на бумаге.[/color]
punishment-examine-pacified = [color=red]Выглядит необычно миролюбивым.[/color]
punishment-paper-write-blocked = Вы не можете писать на бумаге!
punishment-chat-channel-muted = Вам ограничен доступ к этому каналу чата администрацией сервера.

# Вебхук наказаний
server-time-mute =
    Временный мут на { $mins } { $mins ->
    [one] минуту
    [few] минуты
    *[other] минут
    }.
server-perma-mute = Перманентный мут
server-mute-string =
    > **Нарушитель**
    > **Логин:** ``{ $targetName }``
    > **Discord:** { $targetLink }

    > **Администратор**
    > **Логин:** ``{ $adminName }``
    > **Discord:** { $adminLink }

    > **Время**
    > **Выдан:** { $TimeNow }
    > **Истекает:** { $expiresString }

    > **Ограничения:** { $roles }

    > **Причина:** { $reason }

    > **Тяжесть:** { $severity }
server-perma-mute-string =
    > **Нарушитель**
    > **Логин:** ``{ $targetName }``
    > **Discord:** { $targetLink }

    > **Администратор**
    > **Логин:** ``{ $adminName }``
    > **Discord:** { $adminLink }

    > **Время**
    > **Выдан:** { $TimeNow }

    > **Ограничения:** { $roles }

    > **Причина:** { $reason }

    > **Тяжесть:** { $severity }

punishment-role-mute-local = Локальный чат
punishment-role-mute-whisper = Шёпот
punishment-role-mute-radio = Рация
punishment-role-mute-looc = LOOC
punishment-role-mute-ooc = OOC
punishment-role-mute-emotes = Эмоции
punishment-role-mute-dead = Мёртвый чат
punishment-role-mute-paper = Письмо на бумаге
punishment-role-pacifism = Пацифизм
