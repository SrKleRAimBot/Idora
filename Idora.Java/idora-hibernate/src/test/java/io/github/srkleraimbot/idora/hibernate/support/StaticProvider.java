package io.github.srkleraimbot.idora.hibernate.support;

import io.github.srkleraimbot.idora.IdoraGenerator;
import io.github.srkleraimbot.idora.hibernate.IdoraGeneratorProvider;
import io.github.srkleraimbot.idora.hibernate.entities.Order;
import io.github.srkleraimbot.idora.hibernate.entities.User;

/** A provider named by class, or found through the service loader, the way an application would write one. */
public final class StaticProvider implements IdoraGeneratorProvider {

    /** The one generator every lookup returns. */
    public static final IdoraGenerator IDS = IdoraGenerator.builder()
            .useUuidV7()
            .registerEntities(User.class, Order.class)
            .build();

    @Override
    public IdoraGenerator getIdoraGenerator() {
        return IDS;
    }
}
