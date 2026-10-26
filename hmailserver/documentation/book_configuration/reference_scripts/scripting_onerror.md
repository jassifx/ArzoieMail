---
title: "OnError"
slug: scripting_onerror
parent: reference_scripts
index: 0
is_book: false
---

> **ArzoieMail reference status.** Product prose is maintained for ArzoieMail; inherited operational details still need version-specific review. Current installation, security and migration guides take precedence. See the [documentation status](https://github.com/jassifx/ArzoieMail/blob/master/docs/documentation-status.md) for remaining work.


## Overview

This event is executed directly when an error occurs. The event is executed before any global rules are executed

## Signature

OnError(iSeverity, iCode, sSource, sDescription)

## Parameters

- iSeverity - Error severity. 1 - Critical, 2 - High, 3 - Medium, 4 - Low
- iCode - The ArzoieMail error code.
- sSource - The component which reported the error.
- sDescription - A description of the reported error.

## Result values

- None
