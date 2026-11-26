module github.com/SrKleRAimBot/Idora/Idora.Go/idoragorm

go 1.23

require (
	github.com/SrKleRAimBot/Idora/Idora.Go v1.1.0
	github.com/glebarez/sqlite v1.11.0
	gorm.io/gorm v1.31.2
)

require (
	github.com/dustin/go-humanize v1.0.1 // indirect
	github.com/glebarez/go-sqlite v1.21.2 // indirect
	github.com/google/uuid v1.3.0 // indirect
	github.com/jinzhu/inflection v1.0.0 // indirect
	github.com/jinzhu/now v1.1.5 // indirect
	github.com/mattn/go-isatty v0.0.17 // indirect
	github.com/remyoudompheng/bigfft v0.0.0-20230129092748-24d4a6f8daec // indirect
	golang.org/x/sys v0.7.0 // indirect
	golang.org/x/text v0.20.0 // indirect
	modernc.org/libc v1.22.5 // indirect
	modernc.org/mathutil v1.5.0 // indirect
	modernc.org/memory v1.5.0 // indirect
	modernc.org/sqlite v1.23.1 // indirect
)

// Builds and tests against the core module in this repository. Replace directives apply only
// when this module is the main module, so they are ignored by everyone who depends on it, who
// get the required version above from the module proxy. See README.md, "Releasing".
replace github.com/SrKleRAimBot/Idora/Idora.Go => ../
