# Excel spreadsheets

## What a spreadsheet is for

A spreadsheet is a grid of **cells**, each named by its column letter and row number (B3 is column B, row 3). A cell can
hold a value (a number, text or a date) or a **formula** that calculates a value from other cells. The power of a
spreadsheet is that when an input changes, every formula that depends on it recalculates automatically, so one sheet can
answer "what if?" questions instantly. A **workbook** is a file holding one or more **worksheets** (tabs).

Good spreadsheets keep **inputs, calculations and outputs separate**: raw data and assumptions in one place, formulas that
work on them in another, and summaries or charts at the end. Typing a number directly into a formula ("hard-coding"),
such as =B2*1.07 instead of pointing to a cell that holds the 7 percent tax rate, makes a sheet hard to check and easy to
break when the rate changes.

## Formulas and references

Every formula starts with **=**. It can use arithmetic (+ - * / ^), cell references and **functions** such as SUM,
AVERAGE, MIN, MAX and COUNT. A **range** like A2:A10 means every cell from A2 to A10.

When a formula is copied to another cell, its references shift by the same amount: a **relative reference** (A2) copied
one row down becomes A3. An **absolute reference** uses dollar signs (**$A$2**) and does not change when copied; a **mixed
reference** fixes only the column ($A2) or only the row (A$2). Choosing the right kind is what lets one formula be filled
down a whole column correctly, for example dividing each row's sales by a single total kept in one fixed cell.

Excel follows the usual **order of operations**: brackets first, then powers, multiplication and division, then addition
and subtraction, so =2+3*4 gives 14, not 20.

Common error values tell you what went wrong: **#DIV/0!** (dividing by zero or an empty cell), **#N/A** (a lookup found no
match), **#REF!** (a reference points to a cell that was deleted), **#VALUE!** (the wrong type of data, such as text
where a number was expected) and **#NAME?** (a misspelled function or name). A **circular reference** is a formula that
depends, directly or indirectly, on its own result.

## Logic and lookups

**IF** makes a decision: =IF(B2>=60,"Pass","Fail") returns one value when a condition is true and another when it is
false. AND and OR combine conditions, and IFERROR replaces an error with a chosen value. **SUMIF**, **COUNTIF** and
**AVERAGEIF** (and their plural forms SUMIFS, COUNTIFS) add, count or average only the rows that meet conditions, such as
total sales for one region.

Lookups pull a value from a table. **VLOOKUP** searches the first column of a table and returns a value from a column to
the right; with its last argument FALSE it finds only exact matches. Its weaknesses are that it cannot look to the left
and breaks if columns are inserted. **XLOOKUP**, added in newer versions of Excel, searches any column and returns from
any column, and the older combination **INDEX and MATCH** does the same in every version.

## Working with data

Data works best as a simple **table**: one header row, one record per row, one kind of information per column, with no
blank rows or merged cells in the middle. Turning a range into an Excel **Table** (Ctrl+T) gives it a name, extends
formulas and formatting automatically as rows are added, and lets formulas refer to columns by name.

**Sorting** reorders rows; **filtering** hides rows that do not match. **Data validation** restricts what can be typed in
a cell, for example a drop-down list or a number between 1 and 100, which prevents errors at the source. **Conditional
formatting** colors cells by rule, such as highlighting overdue dates in red.

A **PivotTable** summarizes a large table without formulas: you drag fields into rows, columns and values, and it groups
and totals the data, for example sales by region and month. Because it is a summary of the source, it must be
**refreshed** when the source data changes.

## Charts and presenting results

A chart should match the question. **Column or bar charts** compare categories, **line charts** show change over time,
**pie charts** show parts of a whole (and only work with a few slices), and **scatter charts** show whether two numbers
are related. A chart can mislead: a value axis that does not start at zero can make small differences look huge.

## Avoiding mistakes

Spreadsheet errors are common and can be costly; studies of real business spreadsheets have found errors in a large share
of them. Good habits include labeling inputs and units, keeping assumptions in clearly marked cells, using consistent
formulas down a column, checking totals against an independent figure, using **Trace Precedents** and **Trace Dependents**
to see what a formula relies on, and protecting cells that should not be edited. Excel also stores dates as numbers
(days counted from a starting date), which is why dates can be subtracted to get the number of days between them, and why
a date sometimes appears as a plain number when the cell format is wrong.
