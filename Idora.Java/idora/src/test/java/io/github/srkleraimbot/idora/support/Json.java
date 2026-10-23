package io.github.srkleraimbot.idora.support;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * A minimal recursive-descent JSON parser, enough for {@code spec/test-vectors.json} and nothing
 * more, so the test suite needs no JSON library.
 *
 * <p>Objects become {@link LinkedHashMap}s, arrays {@link List}s, strings {@link String}s,
 * integers {@link Long}s, other numbers {@link Double}s, and {@code null} is {@code null}.
 */
public final class Json {

    private final String text;
    private int position;

    private Json(String text) {
        this.text = text;
    }

    /** Parses a complete JSON document. */
    public static Object parse(String text) {
        Json parser = new Json(text);
        Object value = parser.value();
        parser.skipWhitespace();
        if (parser.position != text.length()) {
            throw parser.error("trailing content");
        }
        return value;
    }

    private Object value() {
        skipWhitespace();
        if (position >= text.length()) {
            throw error("unexpected end of input");
        }
        char c = text.charAt(position);
        switch (c) {
            case '{':
                return object();
            case '[':
                return array();
            case '"':
                return string();
            case 't':
                return literal("true", Boolean.TRUE);
            case 'f':
                return literal("false", Boolean.FALSE);
            case 'n':
                return literal("null", null);
            default:
                if (c == '-' || (c >= '0' && c <= '9')) {
                    return number();
                }
                throw error("unexpected character '" + c + "'");
        }
    }

    private Map<String, Object> object() {
        expect('{');
        Map<String, Object> map = new LinkedHashMap<>();
        skipWhitespace();
        if (peek() == '}') {
            position++;
            return map;
        }
        while (true) {
            skipWhitespace();
            String key = string();
            skipWhitespace();
            expect(':');
            map.put(key, value());
            skipWhitespace();
            char c = next();
            if (c == '}') {
                return map;
            }
            if (c != ',') {
                throw error("expected ',' or '}'");
            }
        }
    }

    private List<Object> array() {
        expect('[');
        List<Object> list = new ArrayList<>();
        skipWhitespace();
        if (peek() == ']') {
            position++;
            return list;
        }
        while (true) {
            list.add(value());
            skipWhitespace();
            char c = next();
            if (c == ']') {
                return list;
            }
            if (c != ',') {
                throw error("expected ',' or ']'");
            }
        }
    }

    private String string() {
        expect('"');
        StringBuilder builder = new StringBuilder();
        while (true) {
            char c = next();
            if (c == '"') {
                return builder.toString();
            }
            if (c != '\\') {
                builder.append(c);
                continue;
            }
            char escape = next();
            switch (escape) {
                case '"': builder.append('"'); break;
                case '\\': builder.append('\\'); break;
                case '/': builder.append('/'); break;
                case 'b': builder.append('\b'); break;
                case 'f': builder.append('\f'); break;
                case 'n': builder.append('\n'); break;
                case 'r': builder.append('\r'); break;
                case 't': builder.append('\t'); break;
                case 'u':
                    builder.append((char) Integer.parseInt(text.substring(position, position + 4), 16));
                    position += 4;
                    break;
                default:
                    throw error("bad escape '\\" + escape + "'");
            }
        }
    }

    private Object number() {
        int start = position;
        while (position < text.length() && "+-0123456789.eE".indexOf(text.charAt(position)) >= 0) {
            position++;
        }
        String number = text.substring(start, position);
        if (number.contains(".") || number.contains("e") || number.contains("E")) {
            return Double.parseDouble(number);
        }
        return Long.parseLong(number);
    }

    private Object literal(String word, Object value) {
        if (!text.startsWith(word, position)) {
            throw error("expected '" + word + "'");
        }
        position += word.length();
        return value;
    }

    private void skipWhitespace() {
        while (position < text.length() && Character.isWhitespace(text.charAt(position))) {
            position++;
        }
    }

    private char peek() {
        if (position >= text.length()) {
            throw error("unexpected end of input");
        }
        return text.charAt(position);
    }

    private char next() {
        char c = peek();
        position++;
        return c;
    }

    private void expect(char expected) {
        if (next() != expected) {
            throw error("expected '" + expected + "'");
        }
    }

    private IllegalArgumentException error(String problem) {
        return new IllegalArgumentException("Invalid JSON at offset " + position + ": " + problem);
    }
}
