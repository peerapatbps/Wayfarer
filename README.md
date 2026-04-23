# Wayfarer

Wayfarer is a headless PM automation collector for extracting, classifying, and reporting maintenance job status from an internal web-based PM system.

## Overview

The project is designed to automate repetitive manual review of PM work items from the internal website by:

- logging in through the normal web flow
- navigating PM pages with a headless browser
- extracting job data from HTML
- classifying job status into report-friendly categories
- preparing structured data for dashboards and management reports

Wayfarer is intended to reduce manual page-by-page checking and support internal progress tracking for operational and management review.

## Project Structure

- `Wayfarer.Worker`
- `Wayfarer.Core`
- `Wayfarer.Playwright`

## Technology Stack

- .NET
- Worker Service
- Playwright for .NET

## Notes

Wayfarer is part of a broader system ecosystem:

- `Uroboros` — backend engine
- `BellBeast` — frontend dashboard
- `Wayfarer` — traveling collector for PM automation
