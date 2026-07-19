interaction-LookAt-name = Look at
interaction-LookAt-description = Stare into the void and see it stare back.
interaction-LookAt-success-self-popup = You look at {THE($target)}.
interaction-LookAt-success-target-popup = You feel {THE($user)} looking at you...
interaction-LookAt-success-others-popup = {THE($user)} looks at {THE($target)}.

# Designed to not be seen by others, only you and your target.  Plays a light sound effect that is very unique to it to catch the attention of the party being eye'd up.
interaction-CheckOut-name = Check out
interaction-CheckOut-description = This lets you check someone out on the down low, only you and they will know you did. Unless they were never sentient to begin with!
interaction-CheckOut-success-self-popup = You are really eyeballing {THE($target)}.
interaction-CheckOut-success-target-popup = You think that {THE($user)} might be checking you out...
interaction-CheckOut-success-others-popup = {THE($user)} eyes up {THE($target)}.

interaction-Hug-name = Hug
interaction-Hug-description = A hug a day keeps the psychological horrors beyond your comprehension away.
interaction-Hug-success-self-popup = You hug {THE($target)}.
interaction-Hug-success-target-popup = {THE($user)} hugs you.
interaction-Hug-success-others-popup = {THE($user)} hugs {THE($target)}.
interaction-Hug-fail-self-popup = You fail to hug {THE($target)}.
interaction-Hug-fail-target-popup = {THE($user)} fails to hug you.

interaction-Pet-name = Pet
interaction-Pet-description = Pet your co-worker to ease their stress.
interaction-Pet-success-self-popup = You pet {THE($target)} on {POSS-ADJ($target)} head.
interaction-Pet-success-target-popup = {THE($user)} pets you on your head.
interaction-Pet-success-others-popup = {THE($user)} pets {THE($target)}.
interaction-Pet-fail-self-popup = You fail to pet {THE($target)}.
interaction-Pet-fail-target-popup = {THE($user)} fails to pet you.

interaction-PetAnimal-name = {interaction-Pet-name}
interaction-PetAnimal-description = Pet an animal.
interaction-PetAnimal-success-self-popup = {interaction-Pet-success-self-popup}
interaction-PetAnimal-success-target-popup = {interaction-Pet-success-target-popup}
interaction-PetAnimal-success-others-popup = {interaction-Pet-success-others-popup}
interaction-PetAnimal-fail-self-popup = {interaction-Pet-fail-self-popup}
interaction-PetAnimal-fail-target-popup = {interaction-Pet-fail-target-popup}

interaction-KnockOn-name = Knock
interaction-KnockOn-description = Knock on the target to attract attention.
interaction-KnockOn-success-self-popup = You knock on {THE($target)}.
interaction-KnockOn-success-target-popup = {THE($user)} knocks on you.
interaction-KnockOn-success-others-popup = {THE($user)} knocks on {THE($target)}.
interaction-KnockOn-fail-self-popup = You fail to knock on {THE($target)}.
interaction-KnockOn-fail-target-popup = {THE($user)} fails to knock on you.

interaction-Rattle-name = Rattle
interaction-Rattle-success-self-popup = You rattle {THE($target)}.
interaction-Rattle-success-target-popup = {THE($user)} rattles you.
interaction-Rattle-success-others-popup = {THE($user)} rattles {THE($target)}.
interaction-Rattle-fail-self-popup = You fail to rattle {THE($target)}.
interaction-Rattle-fail-target-popup = {THE($user)} fails to rattle you.

# The below includes conditionals for if the user is holding an item
interaction-WaveAt-name = Wave at
interaction-WaveAt-description = Wave at the target. If you are holding an item, you will wave it.
interaction-WaveAt-success-self-popup = You wave {$hasUsed ->
    [false] at {THE($target)}.
    *[true] your {$used} at {THE($target)}.
}
interaction-WaveAt-success-target-popup = {THE($user)} waves {$hasUsed ->
    [false] at you.
    *[true] {POSS-PRONOUN($user)} {$used} at you.
}
interaction-WaveAt-success-others-popup = {THE($user)} waves {$hasUsed ->
    [false] at {THE($target)}.
    *[true] {POSS-PRONOUN($user)} {$used} at {THE($target)}.
}
interaction-WaveAt-fail-self-popup = You fail to wave at {THE($target)}.
interaction-WaveAt-fail-target-popup = {THE($user)} fails to wave at you.

interaction-Nod-name = Nod
interaction-Nod-success-self-popup = You nod at {THE($target)}.
interaction-Nod-success-target-popup = {THE($user)} nods at you.
interaction-Nod-success-others-popup = {THE($user)} nods at {THE($target)}.

interaction-Decline-name = Decline
interaction-Decline-success-self-popup = You decline {THE($target)}.
interaction-Decline-success-target-popup = {THE($user)} declines you.
interaction-Decline-success-others-popup = {THE($user)} declines {THE($target)}.

interaction-Bow-name = Bow
interaction-Bow-success-self-popup = You bow to {THE($target)}.
interaction-Bow-success-target-popup = {THE($user)} bows to you.
interaction-Bow-success-others-popup = {THE($user)} bows to {THE($target)}.

interaction-Curtsey-name = Curtsey
interaction-Curtsey-success-self-popup = You curtsey to {THE($target)}.
interaction-Curtsey-success-target-popup = {THE($user)} curtseys to you.
interaction-Curtsey-success-others-popup = {THE($user)} curtseys to {THE($target)}.

interaction-Nom-name = Nom
interaction-Nom-success-self-popup = You nom onto {THE($target)}!
interaction-Nom-success-target-popup = {THE($user)} noms onto you!
interaction-Nom-success-others-popup = {THE($user)} noms onto {THE($target)}!

interaction-Blush-name = Blush
interaction-Blush-success-self-popup = You blush at {THE($target)}!
interaction-Blush-success-target-popup = {THE($user)} blushes at you!
interaction-Blush-success-others-popup = {THE($user)} blushes at {THE($target)}!

interaction-BlushSelf-name = Blush
interaction-BlushSelf-success-self-popup = You blush!
interaction-BlushSelf-success-target-popup = {THE($user)} blushes!
interaction-BlushSelf-success-others-popup = {THE($user)} blushes!
interaction-BlushSelf-fail-self-popup = You fail to blush.

interaction-Wink-name = Wink
interaction-Wink-success-self-popup = You wink at {THE($target)}!
interaction-Wink-success-target-popup = {THE($user)} winks at you!
interaction-Wink-success-others-popup = {THE($user)} winks at {THE($target)}!

interaction-WinkSelf-name = Wink
interaction-WinkSelf-success-self-popup = You wink!
interaction-WinkSelf-success-target-popup = {THE($user)} winks!
interaction-WinkSelf-success-others-popup = {THE($user)} winks!
interaction-WinkSelf-fail-self-popup = You fail to wink.

interaction-Nuzzle-name = Nuzzle
interaction-Nuzzle-success-self-popup = You nuzzle {THE($target)}!
interaction-Nuzzle-success-target-popup = {THE($user)} nuzzles you!
interaction-Nuzzle-success-others-popup = {THE($user)} nuzzles {THE($target)}!
