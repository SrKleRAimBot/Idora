/**
 * Hibernate ORM 6 support for Idora identifiers.
 *
 * <pre>{@code
 * @Entity
 * @EntityPrefix("ord")
 * public class Order {
 *     @Id
 *     @IdoraGeneratedId
 *     private IdoraId id;          // generated on persist: "ord_01k2f9v8ta7m3x0qbrn4dzeh5c"
 *
 *     private IdoraId customerId;  // a VARCHAR column, read back as an IdoraId
 * }
 * }</pre>
 *
 * <ul>
 *   <li>{@link io.github.srkleraimbot.idora.hibernate.IdoraGeneratedId} generates primary keys
 *       with an {@link io.github.srkleraimbot.idora.IdoraGenerator}, configured through
 *       {@link io.github.srkleraimbot.idora.hibernate.IdoraHibernateSettings#GENERATOR}.
 *   <li>{@link io.github.srkleraimbot.idora.hibernate.IdoraTypeContributor}, discovered by
 *       Hibernate automatically, maps every {@link io.github.srkleraimbot.idora.IdoraId}
 *       attribute and query parameter to a {@code VARCHAR} column through
 *       {@link io.github.srkleraimbot.idora.hibernate.IdoraIdJavaType}.
 *   <li>{@link io.github.srkleraimbot.idora.hibernate.IdoraIdConverter} is the portable JPA
 *       alternative, for an explicit {@code @Convert}.
 * </ul>
 */
package io.github.srkleraimbot.idora.hibernate;
