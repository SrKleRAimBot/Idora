package idora

import (
	"errors"
	"strings"
	"testing"
	"unicode/utf8"
)

func TestValidatePrefixDetails(t *testing.T) {
	tests := []struct {
		prefix string
		want   InvalidPrefixError
	}{
		{"", InvalidPrefixError{Reason: ReasonEmpty, Index: -1}},
		{strings.Repeat("a", 17), InvalidPrefixError{Reason: ReasonTooLong, Index: -1, Length: 17}},
		// 16 non-ASCII characters are 32 bytes but not too long: the bad character is reported.
		{strings.Repeat("é", 16), InvalidPrefixError{Reason: ReasonCharacter, Index: 0, Char: 'é'}},
		{strings.Repeat("é", 17), InvalidPrefixError{Reason: ReasonTooLong, Index: -1, Length: 17}},
		// Length is checked before characters.
		{strings.Repeat("_", 17), InvalidPrefixError{Reason: ReasonTooLong, Index: -1, Length: 17}},
		{"a_b", InvalidPrefixError{Reason: ReasonSeparator, Index: 1, Char: '_'}},
		// Indexes count characters, not bytes.
		{"éa-", InvalidPrefixError{Reason: ReasonCharacter, Index: 0, Char: 'é'}},
		{"ab\xffc", InvalidPrefixError{Reason: ReasonCharacter, Index: 2, Char: utf8.RuneError}},
		{"a\x00", InvalidPrefixError{Reason: ReasonCharacter, Index: 1, Char: 0}},
		{"usr\n", InvalidPrefixError{Reason: ReasonCharacter, Index: 3, Char: '\n'}},
	}
	for _, tt := range tests {
		t.Run(tt.prefix, func(t *testing.T) {
			var got *InvalidPrefixError
			if !errors.As(validatePrefix(tt.prefix), &got) {
				t.Fatalf("validatePrefix(%q) did not fail", tt.prefix)
			}
			tt.want.Prefix = tt.prefix
			if *got != tt.want {
				t.Errorf("validatePrefix(%q) = %+v, want %+v", tt.prefix, *got, tt.want)
			}
		})
	}
}

func TestValidatePrefixMessages(t *testing.T) {
	tests := []struct {
		prefix string
		want   []string
	}{
		{"", []string{"cannot be empty", "1 to 16 ASCII letters or digits", `"usr"`}},
		{"abcdefghijklmnopq", []string{`"abcdefghijklmnopq"`, "17 characters", "maximum is 16"}},
		{"user_account", []string{"separator '_' at index 4", "divides the prefix from the value"}},
		{"usr!", []string{"character '!' at index 3", "only ASCII letters and digits"}},
	}
	for _, tt := range tests {
		msg := validatePrefix(tt.prefix).Error()
		if !strings.HasPrefix(msg, "idora: ") {
			t.Errorf("message %q lacks the package prefix", msg)
		}
		for _, w := range tt.want {
			if !strings.Contains(msg, w) {
				t.Errorf("message for %q = %q, want it to contain %q", tt.prefix, msg, w)
			}
		}
		if strings.ContainsAny(msg[len(msg)-1:], ".!") {
			t.Errorf("message %q ends with punctuation", msg)
		}
	}
}

func TestValidatePrefixDoesNotAllocateWhenValid(t *testing.T) {
	if n := testing.AllocsPerRun(1000, func() { _ = validatePrefix("A1b2C3d4E5f6G7h8") }); n != 0 {
		t.Errorf("validatePrefix allocated %v times", n)
	}
}

func TestFoldPrefix(t *testing.T) {
	for in, want := range map[string]string{"usr": "usr", "USR": "usr", "aCC": "acc", "A1b2": "a1b2", "0": "0"} {
		if got := foldPrefix(in); got != want {
			t.Errorf("foldPrefix(%q) = %q, want %q", in, got, want)
		}
	}
}
