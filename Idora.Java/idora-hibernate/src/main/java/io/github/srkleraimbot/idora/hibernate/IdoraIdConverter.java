package io.github.srkleraimbot.idora.hibernate;

import io.github.srkleraimbot.idora.IdoraId;
import jakarta.persistence.AttributeConverter;
import jakarta.persistence.Converter;

/**
 * A portable JPA converter between {@link IdoraId} and its text, for an explicit
 * {@code @Convert}.
 *
 * <pre>{@code
 * @Convert(converter = IdoraIdConverter.class)
 * private IdoraId customerId;
 * }</pre>
 *
 * <p>Under Hibernate you do not need it: {@link IdoraTypeContributor} already maps every
 * {@code IdoraId} attribute, and also covers identifiers and query parameters, which a converter
 * cannot. It is here for code that must stay provider-neutral, and for an attribute declared with an
 * explicit converter by convention.
 *
 * <p>It is not auto-applied, so that putting this jar on the class path never changes a mapping
 * silently. To apply it everywhere, declare an auto-applied subclass in a package your persistence
 * unit scans:
 *
 * <pre>{@code
 * @Converter(autoApply = true)
 * public class IdoraIdAutoConverter extends IdoraIdConverter {}
 * }</pre>
 *
 * <p>A database value that is not a conforming identifier fails the read with an
 * {@link io.github.srkleraimbot.idora.InvalidIdException} naming the text.
 */
@Converter
public class IdoraIdConverter implements AttributeConverter<IdoraId, String> {

    /** Creates the converter; JPA instantiates it. */
    public IdoraIdConverter() {
    }

    /**
     * Returns the identifier's text.
     *
     * @param attribute the identifier, or {@code null}
     * @return its text, or {@code null}
     */
    @Override
    public String convertToDatabaseColumn(IdoraId attribute) {
        return attribute == null ? null : attribute.toString();
    }

    /**
     * Parses the column's text, strictly.
     *
     * @param dbData the text, or {@code null}
     * @return the identifier, or {@code null}
     * @throws io.github.srkleraimbot.idora.InvalidIdException if the text is not a conforming
     *     identifier
     */
    @Override
    public IdoraId convertToEntityAttribute(String dbData) {
        return dbData == null ? null : IdoraId.parse(dbData);
    }
}
