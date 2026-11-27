package io.github.srkleraimbot.idora.spring.domain;

import org.springframework.boot.autoconfigure.AutoConfigurationPackage;
import org.springframework.context.annotation.Configuration;

/** Stands in for an application's @SpringBootApplication class: it names this package as the application's. */
@Configuration(proxyBeanMethods = false)
@AutoConfigurationPackage
public class DomainApplication {
}
