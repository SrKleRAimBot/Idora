module github.com/SrKleRAimBot/Idora/Idora.Go/idorapgx

go 1.23.0

require (
	github.com/SrKleRAimBot/Idora/Idora.Go v1.1.0
	github.com/jackc/pgx/v5 v5.7.6
)

// Builds and tests against the core module in this repository. Replace directives apply only
// when this module is the main module, so they are ignored by everyone who depends on it, who
// get the required version above from the module proxy. See README.md, "Releasing".
replace github.com/SrKleRAimBot/Idora/Idora.Go => ../
