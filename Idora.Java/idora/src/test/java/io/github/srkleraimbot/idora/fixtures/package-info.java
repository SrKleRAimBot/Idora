/**
 * A well-formed set of annotated entity types, scanned whole by the happy-path tests, plus enough
 * unannotated ones to prove the scan is selective. Every prefix starts with {@code fx}, so a stray
 * registration from elsewhere is obvious in a failing assertion.
 *
 * <p>Deliberately no clashing or malformed prefixes: those live in
 * {@code io.github.srkleraimbot.idora.unscanned}, which no test scans whole, mirroring how
 * Idora.NET keeps them out of its scanned fixtures assembly.
 */
package io.github.srkleraimbot.idora.fixtures;
