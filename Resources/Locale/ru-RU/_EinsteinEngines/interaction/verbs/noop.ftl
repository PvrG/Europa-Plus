interaction-LookAt-name = Осмотреть
interaction-LookAt-description = Уставься в пустоту и увидь, как она смотрит в ответ.
interaction-LookAt-success-self-popup = Вы уставились на {THE($target)}.
interaction-LookAt-success-target-popup = Вы чувствуете, как {THE($user)} пристально смотрит на вас...
interaction-LookAt-success-others-popup = {THE($user)} пристально смотрит на {THE($target)}.

interaction-Hug-name = Обнять
interaction-Hug-description = Объятие в день — и психологические ужасы за гранью понимания будут держаться подальше.
interaction-Hug-success-self-popup = Вы обнимаете {THE($target)}.
interaction-Hug-success-target-popup = {THE($user)} обнимает вас.
interaction-Hug-success-others-popup = {THE($user)} обнимает {THE($target)}.

interaction-KnockOn-name = Стучать
interaction-KnockOn-description = Постучите по цели, чтобы привлечь внимание.
interaction-KnockOn-success-self-popup = Вы стучите по {THE($target)}.
interaction-KnockOn-success-target-popup = {THE($user)} стучит по вам.
interaction-KnockOn-success-others-popup = {THE($user)} стучит по {THE($target)}.

interaction-WaveAt-name = Помахать
interaction-WaveAt-description = Помашите цели. Если вы держите предмет, вы будете махать им.
interaction-WaveAt-success-self-popup = Вы машете {$hasUsed ->
[false] {THE($target)}.
*[true] своим {$used} {THE($target)}.
}
interaction-WaveAt-success-target-popup = {THE($user)} машет {$hasUsed ->
[false] вам.
*[true] своим {$used} вам.
}
interaction-WaveAt-success-others-popup = {THE($user)} машет {$hasUsed ->
[false] {THE($target)}.
*[true] своим {$used} {THE($target)}.
}