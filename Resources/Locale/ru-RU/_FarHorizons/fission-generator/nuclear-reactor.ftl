### Popups
reactor-smoke-start = {CAPITALIZE(THE($owner))} начинает дымить!
reactor-smoke-stop = {CAPITALIZE(THE($owner))} перестаёт дымить.
reactor-fire-start = {CAPITALIZE(THE($owner))} охватывает пламя!
reactor-fire-stop = {CAPITALIZE(THE($owner))} перестаёт гореть.

reactor-unanchor-melted = Вы не можете открутить {THE($owner)}, корпус намертво вплавился в пол!
reactor-unanchor-warning = Вы не можете открутить {THE($owner)}, пока внутри есть топливо или температура выше 80°C!
reactor-anchor-warning = Неподходящее место для крепления.

### Messages
reactor-smoke-start-message = ВНИМАНИЕ: {CAPITALIZE(THE($owner))} нагрелся до опасной температуры: {$temperature} K. Срочно примите меры во избежание коллапса активной зоны!
reactor-smoke-stop-message = {CAPITALIZE(THE($owner))} остыл ниже опасного порога температуры.
reactor-fire-start-message = ВНИМАНИЕ: {CAPITALIZE(THE($owner))} достиг КРИТИЧЕСКОЙ температуры: {$temperature} K. КОЛЛАПС РЕАКТОРА НЕИЗБЕЖЕН!
reactor-fire-stop-message = {CAPITALIZE(THE($owner))} остыл ниже критического порога. Авария предотвращена.

reactor-temperature-dangerous-message = {CAPITALIZE(THE($owner))}: опасная температура ({$temperature} K).
reactor-temperature-critical-message = {CAPITALIZE(THE($owner))}: критическая температура ({$temperature} K).
reactor-temperature-cooling-message = {CAPITALIZE(THE($owner))} охлаждается: {$temperature} K.

reactor-melting-announcement = Внимание: ядерный реактор колонии выходит из строя! Рекомендуется немедленная эвакуация из инженерного отсека.
reactor-melting-announcement-sender = Аварийная служба реактора

reactor-meltdown-announcement = КРИТИЧЕСКАЯ АВАРИЯ: Произошло разрушение активной зоны ядерного реактора колонии! Зафиксирован катастрофический выброс радиации и пожары теплоносителя. Немедленно покиньте прилегающие секторы!
reactor-meltdown-announcement-sender = Служба ядерной безопасности

### UI
comp-nuclear-reactor-ui-locked = Заблокировано
comp-nuclear-reactor-ui-insert-button = Вставить
comp-nuclear-reactor-ui-remove-button = Извлечь
comp-nuclear-reactor-ui-eject-button = Сброс

comp-nuclear-reactor-ui-view-change = Сменить вид
comp-nuclear-reactor-ui-view-temp = Температурный режим
comp-nuclear-reactor-ui-view-neutron = Нейтронный поток
comp-nuclear-reactor-ui-view-fuel = Состояние топлива

comp-nuclear-reactor-ui-status-panel = Статус реактора
comp-nuclear-reactor-ui-reactor-temp = Температура
comp-nuclear-reactor-ui-reactor-rads = Радиация
comp-nuclear-reactor-ui-reactor-therm = Тепловая мощность
comp-nuclear-reactor-ui-reactor-control = Регулирующие стержни
comp-nuclear-reactor-ui-therm-format = { POWERWATTS($power) }

comp-nuclear-reactor-ui-footer-left = ОПАСНОСТЬ: ВЫСОКИЙ УРОВЕНЬ РАДИАЦИИ
comp-nuclear-reactor-ui-footer-right = 1.0 РЕД. 1
