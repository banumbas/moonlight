ban-panel-tabs-punishments = Punishments
ban-panel-punishment = Punishment
ban-panel-punishment-channels-label = Mute Channels
ban-panel-punishment-channel-local = Local
ban-panel-punishment-channel-whisper = Whisper
ban-panel-punishment-channel-radio = Radio
ban-panel-punishment-channel-looc = LOOC
ban-panel-punishment-channel-ooc = OOC
ban-panel-punishment-channel-emotes = Emotes
ban-panel-punishment-channel-dead = Dead

ban-panel-punishment-other-label = Other
ban-panel-punishment-paper = Mute Paper
ban-panel-punishment-pacifism = Pacifism

punishment-examine-muted-channels = [color=red]Is muted from channels: {$channels}[/color]
punishment-examine-paper-muted = [color=red]Cannot write on paper.[/color]
punishment-examine-pacified = [color=red]Appears unusually peaceful.[/color]
punishment-paper-write-blocked = You cannot write on paper!
punishment-chat-channel-muted = You are muted in this chat channel by server administration.

# Punishment Webhook
server-time-mute =
    Temporary mute for { $mins } { $mins ->
        [one] minute
       *[other] minutes
    }.
server-perma-mute = Permanent mute
server-mute-string =
    > **Offender**
    > **Login:** ``{ $targetName }``
    > **Discord:** { $targetLink }

    > **Administrator**
    > **Login:** ``{ $adminName }``
    > **Discord:** { $adminLink }

    > **Time**
    > **Extended:** { $TimeNow }
    > **Expires:** { $expiresString }

    > **Restrictions:** { $roles }

    > **Reason:** { $reason }

    > **Severity Level:** { $severity }
server-perma-mute-string =
    > **Offender**
    > **Login:** ``{ $targetName }``
    > **Discord:** ``{ $targetLink }``

    > **Administrator**
    > **Login:** ``{ $adminName }``
    > **Discord:** { $adminLink }

    > **Time**
    > **Extended:** { $TimeNow }

    > **Restrictions:** { $roles }

    > **Reason:** { $reason }

    > **Severity Level:** { $severity }

punishment-role-mute-local = Local chat
punishment-role-mute-whisper = Whisper
punishment-role-mute-radio = Radio
punishment-role-mute-looc = LOOC
punishment-role-mute-ooc = OOC
punishment-role-mute-emotes = Emotes
punishment-role-mute-dead = Dead chat
punishment-role-mute-paper = Paper writing
punishment-role-pacifism = Pacifism

