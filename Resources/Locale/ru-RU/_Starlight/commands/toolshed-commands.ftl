command-description-radio-addcustom =
    Добавьте пользовательский канал к указанному компоненту в передаваемом объекте. Укажите true или false в конце, чтобы убедиться, что компонент существует.
command-description-radio-remcustom =
    Удалите пользовательский канал с заданным идентификатором из указанного компонента в передаваемом объекте.
command-description-container-insertentity =
    Вставляет заданный объект в указанный контейнер в передаваемом объекте.
command-description-container-create =
    Создает новый контейнер в передаваемом объекте.
command-description-container-createslot =
    Создает новый слот контейнера в конвейерном объекте.
command-description-container-delete =
    Удаляет контейнер в конвейерном объекте.
command-description-container-drop =
    Удаляет все содержащиеся объекты из указанного контейнера в передаваемом объекте.
command-description-container-dropandget =
    Удаляет все содержащиеся сущности из указанного контейнера в передаваемой по конвейеру сущности и возвращает все удаленные элементы вместо переданной по конвейеру сущности.
command-description-container-dropanddelete =
    Удаляет все содержащиеся объекты из указанного контейнера в передаваемом по конвейеру объекте, а затем удаляет контейнер.
command-description-container-getentities =
    Получает все объекты в заданном контейнере в передаваемом объекте.
command-description-container-getcontaining =
    Получает все контейнеры, содержащие в данный момент передаваемый объект.
command-description-container-getoutercontainer =
    Получает самый внешний контейнер, содержащий переданный по конвейеру объект.
command-description-container-getowner =
    Получает сущность, владеющую указанным контейнером.
command-description-solution-adjcapacity =
    Регулирует мощность данного раствора.
command-description-solution-adjtemperature =
    Регулирует мощность данного раствора.
command-description-solution-adjthermalenergy =
    Регулирует мощность данного раствора.
command-description-solution-create=
    Создает новое решение с заданным именем в передаваемом объекте. Возвращает существующее решение, если оно уже существует.
command-description-solution-delete=
    Удаляет указанное решение в конвейерном объекте.
### Starlight (upstream #39080)
command-description-subtlemessage =
    Отправляет тонкое сообщение всем входным объектам.
command-description-grid-getplayers =
    Получает всех игроков в конвейерной сетке(ях)
command-description-grid-get =
    Получает сетку(и), на которой стоят игроки по конвейеру.
command-description-grid-getstation =
    Получает колонию(и), на которой стоят пересылаемые игроки, или колонию ​​самого объекта, если сетка подключена по конвейеру.
command-description-crewmanifest-addto =
    Добавляет передаваемый объект в манифест экипажа указанной колонии.
command-description-crewmanifest-removefrom =
    Удаляет передаваемый объект из манифеста экипажа указанной колонии.
command-description-crewmanifest-addplayer =
    Добавляет указанного игрока в манифест(ы) экипажа трубопроводной колонии(й).
command-description-crewmanifest-removeplayer =
    Удаляет указанного игрока из манифеста(ов) экипажа трубопроводной колонии(й).
command-description-storage-reshape =
    Изменяет форму хранилища на основе данных, полученных с помощью команды box2iconstructor.
command-description-box2iconstructor-new =
    Создайте новое определение списка Box2i для объекта, объедините его с командами box2iconstructor:add, а затем выполните команду, которая этого требует.
command-description-box2iconstructor-add =
    Добавьте новый Box2i к существующему определению. Прежде чем использовать это, вызовите box2iconstructor:new.
command-description-box2iconstructor-clean =
    Очистите неиспользуемое определение списка Box2i для объекта.
command-description-vector2dataconstructor-new =
    Создайте новое определение списка Vector2 для объекта, объедините его с командами Vector2dataconstructor:add, а затем выполните команду, которая этого требует.
command-description-vector2dataconstructor-add =
    Добавьте новый Vector2 к существующему определению. Прежде чем использовать это, вызовите вектор2dataconstructor:new.
command-description-vector2dataconstructor-clean =
    Очистите неиспользуемое определение списка Vector2 в объекте.
command-description-job-set =
    Изменяет задание передаваемого объекта.
command-description-job-delset =
    Изменяет задание передаваемого объекта, удаляя, а затем устанавливая задание, чтобы воспроизводился брифинг.
command-description-ccomp-ensure =
    Гарантирует, что все клиенты добавляют компонент с указанным именем в сущность, если она существует.
command-description-ccomp-write =
    Попытайтесь заставить всех клиентов записать что-нибудь в клиентский компонент.
command-description-ccomp-rm =
    Гарантирует, что все клиенты удалят компонент с указанным именем из сущности, если она существует.
command-description-globalsound-play =
    Воспроизводите звук глобально для передаваемых по конвейеру объектов или сеансов.
command-description-polymorph-begin =
    Маркер, начинающий последовательность инструкций по настройке полиморфа, прикрепляет к объекту PolymorphSetupComponent.
command-description-polymorph-setproto =
    Установите прототип, в который будет трансформироваться сущность.
command-description-polymorph-seteffect =
    Установите прототип так, чтобы он появлялся поверх полиморфированного объекта. Обычно это используется для создания специальных эффектов.
command-description-polymorph-setdelay =
    Установите время ожидания в секундах, прежде чем можно будет снова активировать этот конкретный полиморф.
command-description-polymorph-setduration =
    Установите продолжительность действия полиморфа в секундах, прежде чем он автоматически вернется в исходное состояние.
command-description-polymorph-setforced =
    Установлено так, чтобы полиморф не мог быть активирован или отменен самим объектом.
command-description-polymorph-settransferdamage =
    Устанавливается для переноса урона от текущего объекта к полиморфированному объекту.
command-description-polymorph-settransfername =
    Установите, чтобы полиморфированная сущность наследовала имя оригинала.
command-description-polymorph-settransferappearance =
    Установите, следует ли переносить такие параметры, как волосы, цвет кожи, рост и т. д., в полиморфный объект.
command-description-polymorph-setinventory =
    Устанавливается, чтобы определить, как инвентарь сущности будет перенесен в полиморфную сущность.
command-description-polymorph-setrevertoncrit =
    Установите, следует ли отменить полиморф, когда объект входит в критическое состояние или нет.
command-description-polymorph-setrevertondeath =
    Установите, следует ли отменить полиморф, когда объект будет убит или нет.
command-description-polymorph-setrevertondelete =
    Установите, следует ли возвращать полиморф при удалении объекта или нет.
command-description-polymorph-setrevertoneat =
    Установите, следует ли отменить полиморф, когда объект съеден или нет.
command-description-polymorph-setallowrepeats =
    Установите, разрешать ли повторяющиеся полиморфы или нет.
command-description-polymorph-setignoreallowrepeats =
    Установите, чтобы позволить полиморфу произойти, даже если AllowRepeatedMorphs имеет значение true.
command-description-polymorph-setcooldown =
    Установите время восстановления в секундах, прежде чем может произойти следующий полиморф.
command-description-polymorph-setentersound =
    Установите звук, который воспроизводится при входе в полиморф.
command-description-polymorph-setexitsound =
    Установите звук, который воспроизводится при выходе из полиморфа.
command-description-polymorph-clearentersound =
    Убрать звук, который воспроизводится при входе в полиморф.
command-description-polymorph-clearexitsound =
    Убрать звук, который воспроизводится при выходе из полиморфа.
command-description-polymorph-setenterpopup =
    Установите всплывающее окно, которое появляется при входе в полиморф.
command-description-polymorph-setexitpopup =
    Установите всплывающее окно, которое появляется при выходе из полиморфа.
command-description-polymorph-clearcopycomp =
    Очистите список компонентов для копирования в полиморф.
command-description-polymorph-addcopycomp =
    Добавьте запись в список компонентов для копирования в полиморф.
command-description-polymorph-rmcopycomp =
    Удалите запись из списка компонентов для копирования в полиморф.
command-description-polymorph-apply =
    Мгновенно примените полиморф и закончите.
command-description-polymorph-applyget =
    Мгновенно примените полиморф и завершите операцию, вернув новую сущность.
command-description-polymorph-addaction =
    Добавьте действие полиморфирования к сущности, используя текущую цепочку настроек полиморфа. Вероятно, вам следует позже вызвать полиморф: применить или полиморф: закончить.
command-description-polymorph-addactionproto =
    Добавьте к сущности прототип действия полиморфа.
command-description-polymorph-rmaction =
    Удалите действие полиморфа из объекта, который был добавлен с помощью полиморфа:добавление.
command-description-polymorph-rmactionproto =
    Удалите прототип действия полиморфа из сущности.
command-description-polymorph-revert =
    Вернитесь к предыдущему объекту x, если это возможно.
command-description-polymorph-reset =
    Сбросьте полиморф объекта в исходное состояние.
command-description-polymorph-finish =
    Отмечает эту цепочку установки полиморфа как завершенную, очищая и удаляя компонент.
command-description-vv-open =
    Откройте окно ViewVariables передаваемого объекта или пути.
command-description-vv-write =
    Измените значение пути с помощью VV (просмотр переменных). В качестве значения можно использовать переменную, но это должна быть сериализованная строка.
command-description-vv-owrite =
    Измените значение пути с помощью VV (просмотр переменных). В качестве значения можно использовать необработанную переменную.
command-description-vv-read =
    Распечатайте значение пути, используя VV (просмотр переменных).
command-description-vv-rsave =
    Получите значение пути с помощью VV (просмотр переменных). Можно сохранить в переменную.
command-description-vv-rsaveraw =
    Получите значение пути с помощью VV (просмотр переменных). Можно сохранить в переменную. Сохраняет необработанное значение вместо сериализованной строки.
command-description-mind-wipe =
    Стирает разум игрока или сущности. Обратите внимание: это сделает их игру неиграбельной, пока вы не дадите им новый разум.
command-description-mind-takeover =
    Непосредственно захватите толпу, создав разум, если он не существует, и заставив существо стать разумным.
command-description-mind-takeoverwipe =
    Сотрите свой разум, а затем захватите сущность. Это прояснит все роли и цели в сознании.
command-description-mind-controlwipe =
    Сотрите разум целевого игрока и заставьте его контролировать передаваемую по каналу сущность, создав новый разум и сделав сущность разумной.
command-description-killsign-set =
    Примените сигнал уничтожения к объекту, используя указанное состояние.
command-description-killsign-list =
    Перечисляет все доступные знаки уничтожения.
command-description-killsign-rm =
    Удалить сигнал уничтожения с объекта
command-description-fixinput =
    Обновляет входной контекст сеанса сущности.
command-description-faction-add =
    Добавьте фракцию к этой сущности.
command-description-faction-remove =
    Удалить фракцию из этой сущности.
command-description-faction-aggro =
    Сделайте эту сущность агрессивной по отношению к целевой сущности.
command-description-faction-deaggro =
    Сделайте эту сущность более не агрессивной по отношению к целевой сущности.
command-description-faction-ignore =
    Заставьте эту сущность и целевую сущность игнорировать друг друга.
command-description-faction-unignore =
    Заставьте эту сущность и целевую сущность больше не игнорировать друг друга.
command-description-faction-clear =
    Очистите фракции этой сущности.
command-description-npc-sethtn =
    Создает NPC для объекта и устанавливает для него состав HTN.
command-description-npc-setenabled =
    Включите или отключите поведение HTN этого NPC.
command-description-stationinit-begin =
    Начните процесс инициализации новой промежуточной колонии. Прикрепляет BecomesStationMidRoundComponent к сетке.
command-description-stationinit-setid =
    Установите идентификатор колонии. Это сделано для предотвращения дублирования.
command-description-stationinit-clearbaseprotos =
    Очистите список прототипов базовых колоний.
command-description-stationinit-addbaseproto =
    Добавьте прототип базовой колонии для использования.
command-description-stationinit-rmbaseproto =
    Удалить прототип базовой колонии из использования.
command-description-stationinit-setallowftl =
    Установить, позволяющий любому человеку перемещаться на сверхсветовой скорости к карте, на которой находится эта колония.
command-description-stationinit-setuseemergencyshuttle =
    Установите создание аварийного шаттла для использования в конце раунда.
command-description-stationinit-setusearmories =
    Установите нерестовые арсеналы, которые можно отправить в колонию ​​с помощью команды арсенала.
command-description-stationinit-setusearrivals =
    Установить создание шаттла прибытия в эту колонию.
command-description-stationinit-setallowdungeonspawns =
    Набор, позволяющий создавать подземелья, такие как VGroid.
command-description-stationinit-setallowcargo =
    Набор, позволяющий появляться грузовым шаттлам и ATS.
command-description-stationinit-clearallowedgridspawns =
    Очистите список сеточных спавнов, которым разрешено появляться из базовых прото.
command-description-stationinit-addallowedgridspawn =
    Добавьте сетку, которой разрешено появляться из базовых прототипов.
command-description-stationinit-rmallowedgridspawn =
    Удалите сетку, которой разрешено появляться из базовых прототипов.
command-description-stationinit-setemergencyshuttlepath =
    Установите переопределение, которое будет использоваться для сетки аварийного челнока.
command-description-stationinit-clearjobs =
    Удалить все задания в этой колонии.
command-description-stationinit-addjob =
    Добавьте новую работу в эту колонию.
command-description-stationinit-rmjob =
    Удалить задание из этой колонии.
command-description-stationinit-setallowevents =
    Установите разрешение событий в эту колонию.
command-description-stationinit-setdovariationpass =
    Установите, позволяющее запускать вариационный проход в начале раунда в вновь созданной колонии.
command-description-stationinit-namegrid =
    Переименуйте целевую сетку. Имя сетки будет использоваться в качестве имени колонии при инициализации.
command-description-stationinit-initialize =
    Завершите настройку и инициализируйте колонию.
command-description-stationinit-initializeget =
    Завершите настройку и инициализируйте колонию ​​и верните вновь созданный объект колонии.
command-description-aitakeover =
    Заставьте передаваемую по конвейеру сущность взять на себя управление целевым ядром ИИ.
command-description-mobthreshold-initialize =
    Правильно инициализирует новый порог моба для объекта.
command-description-corporeal-on =
    Делает вашего призрака видимым и дает ему возможность говорить.
command-description-corporeal-off =
    Делает вашего призрака невидимым и лишает способности говорить.
command-description-markup-adddesc =
    Добавьте текст разметки в описание передаваемого объекта с заданным идентификатором.
command-description-markup-editdesc =
    Отредактируйте строку текста разметки из описания передаваемого объекта с заданным идентификатором.
command-description-markup-rmdesc =
    Удалите строку текста разметки из описания передаваемого объекта с заданным идентификатором.
command-description-markup-cleardesc =
    Удаляет все дополнительные строки текста разметки из описания передаваемого объекта.
command-description-markup-listdesc =
    Перечисляет все тексты разметки описания передаваемого объекта и их идентификаторы.
command-description-atmos-add =
    Добавляет атмосферу трубопроводной сетке.
command-description-atmos-fix =
    Исправьте атмосферу трубопроводной сети.
command-description-atmos-rejoin =
    Попытайтесь заставить атмосферное устройство воссоединиться с атмосферой.
command-description-jobs-makeunlimited =
    Сделайте слот для работы неограниченным.
command-description-jobs-makelimited =
    Ограничьте количество рабочих мест. Позволяет сбросить значение до 0 или до любого значения счетчика в середине раунда.
