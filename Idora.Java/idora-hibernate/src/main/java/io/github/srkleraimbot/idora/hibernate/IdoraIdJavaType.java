package io.github.srkleraimbot.idora.hibernate;

import io.github.srkleraimbot.idora.IdoraId;
import org.hibernate.dialect.Dialect;
import org.hibernate.type.SqlTypes;
import org.hibernate.type.descriptor.WrapperOptions;
import org.hibernate.type.descriptor.java.AbstractClassJavaType;
import org.hibernate.type.descriptor.jdbc.JdbcType;
import org.hibernate.type.descriptor.jdbc.JdbcTypeIndicators;

/**
 * Hibernate's description of {@link IdoraId}: an immutable value stored as its text in a
 * {@code VARCHAR} column.
 *
 * <p>{@link IdoraTypeContributor} registers it with every Hibernate instance that has
 * {@code idora-hibernate} on its class path, so {@code IdoraId} attributes, identifiers and query
 * parameters need no annotation. Without it, Hibernate would see a {@link java.io.Serializable}
 * class it did not know and store it as a serialized blob.
 *
 * <p>Values are read back with the strict {@link IdoraId#parse(CharSequence)}, so a column holding
 * something other than a conforming identifier fails the read with an
 * {@link io.github.srkleraimbot.idora.InvalidIdException} naming the text.
 *
 * <p>A column generated from the mapping is {@value #DEFAULT_LENGTH} characters wide, the longest
 * an identifier can be. Declare the column yourself, with
 * {@code @Column(length = 30)} or your own migration, to size it for your prefix and format; see the
 * core README's "Sizing your database column".
 */
public final class IdoraIdJavaType extends AbstractClassJavaType<IdoraId> {

    private static final long serialVersionUID = 1L;

    /**
     * The longest identifier: a 16-character prefix, the separator and a 32-character UUIDv7 value.
     */
    public static final int DEFAULT_LENGTH = 49;

    /** The shared instance; the type is stateless. */
    public static final IdoraIdJavaType INSTANCE = new IdoraIdJavaType();

    /** Creates the type. Prefer {@link #INSTANCE}; this constructor exists for {@code @JavaType}. */
    public IdoraIdJavaType() {
        super(IdoraId.class);
    }

    /**
     * Identifiers are stored as text.
     *
     * @param indicators what Hibernate knows about the column
     * @return the dialect's {@code VARCHAR} type
     */
    @Override
    public JdbcType getRecommendedJdbcType(JdbcTypeIndicators indicators) {
        return indicators.getJdbcType(SqlTypes.VARCHAR);
    }

    /**
     * The width of a generated column.
     *
     * @param dialect the database dialect
     * @param jdbcType the column's JDBC type
     * @return {@value #DEFAULT_LENGTH}
     */
    @Override
    public long getDefaultSqlLength(Dialect dialect, JdbcType jdbcType) {
        return DEFAULT_LENGTH;
    }

    /**
     * {@link IdoraId#equals(Object)} and {@link IdoraId#hashCode()} are value-based.
     *
     * @return {@code true}
     */
    @Override
    public boolean useObjectEqualsHashCode() {
        return true;
    }

    /**
     * Returns the identifier's text.
     *
     * @param value the identifier
     * @return its text
     */
    @Override
    public String toString(IdoraId value) {
        return value.toString();
    }

    /**
     * Parses an identifier, strictly.
     *
     * @param string the text
     * @return the identifier
     * @throws io.github.srkleraimbot.idora.InvalidIdException if the text is not a conforming
     *     identifier
     */
    @Override
    public IdoraId fromString(CharSequence string) {
        return IdoraId.parse(string);
    }

    /**
     * Converts an identifier to the type JDBC asks for.
     *
     * @param value the identifier, or {@code null}
     * @param type {@link String}, {@link CharSequence} or {@link IdoraId}
     * @param options the wrapper options
     * @param <X> the requested type
     * @return the converted value, or {@code null}
     */
    @Override
    @SuppressWarnings("unchecked")
    public <X> X unwrap(IdoraId value, Class<X> type, WrapperOptions options) {
        if (value == null) {
            return null;
        }
        if (type.isAssignableFrom(IdoraId.class)) {
            return (X) value;
        }
        if (type.isAssignableFrom(String.class)) {
            return (X) value.toString();
        }
        throw unknownUnwrap(type);
    }

    /**
     * Converts what JDBC returned to an identifier.
     *
     * @param value an {@link IdoraId}, any {@link CharSequence}, or {@code null}
     * @param options the wrapper options
     * @param <X> the type JDBC returned
     * @return the identifier, or {@code null}
     * @throws io.github.srkleraimbot.idora.InvalidIdException if the text is not a conforming
     *     identifier
     */
    @Override
    public <X> IdoraId wrap(X value, WrapperOptions options) {
        if (value == null) {
            return null;
        }
        if (value instanceof IdoraId) {
            return (IdoraId) value;
        }
        if (value instanceof CharSequence) {
            return IdoraId.parse((CharSequence) value);
        }
        throw unknownWrap(value.getClass());
    }
}
