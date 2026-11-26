package io.github.srkleraimbot.idora.hibernate;

import org.hibernate.boot.model.TypeContributions;
import org.hibernate.boot.model.TypeContributor;
import org.hibernate.service.ServiceRegistry;

/**
 * Registers {@link IdoraIdJavaType} with Hibernate, so that {@link io.github.srkleraimbot.idora.IdoraId}
 * attributes are mapped to {@code VARCHAR} columns without any annotation.
 *
 * <p>Hibernate discovers it through {@link java.util.ServiceLoader} as soon as
 * {@code idora-hibernate} is on the class path; there is nothing to configure.
 */
public final class IdoraTypeContributor implements TypeContributor {

    /** Called by the {@link java.util.ServiceLoader}. */
    public IdoraTypeContributor() {
    }

    /**
     * Contributes {@link IdoraIdJavaType#INSTANCE}.
     *
     * @param contributions where types are contributed
     * @param serviceRegistry Hibernate's services; unused
     */
    @Override
    public void contribute(TypeContributions contributions, ServiceRegistry serviceRegistry) {
        contributions.contributeJavaType(IdoraIdJavaType.INSTANCE);
    }
}
