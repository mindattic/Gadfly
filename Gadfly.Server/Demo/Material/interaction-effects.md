# Interaction Effects in Regression

These notes support the demonstration assessment in statistics (docs/MASTER-PLAN.md Part 7.2): the learning
objective is to understand interaction effects in regression, at the level of an introductory course.

## Main effects in a regression model

A regression model describes how an outcome changes with one or more independent variables. In a model
predicting exam score from hours studied and hours slept, the coefficient of hours studied is the expected
change in exam score for one more hour of study, holding hours slept constant.

When a model has only main effects, the effect of each independent variable is the same at every value of
the other variables. The lines for different amounts of sleep are parallel: an extra hour of study adds the
same number of points whether the student slept five hours or nine.

## Interaction effects

An interaction effect means that the relationship between an independent variable and the outcome depends on
the value of another variable. If extra study helps well-rested students more than tired students, then the
effect of study hours on exam score depends on sleep, and study and sleep interact.

An interaction is added to a regression model as a product term, for example study multiplied by sleep. The
coefficient of the product term says how much the slope of one variable changes for each one-unit increase in
the other variable. With an interaction, the lines for different amounts of sleep are no longer parallel.

When a model includes an interaction, the coefficient of study alone is the effect of study when sleep is
zero, which may not be a meaningful value. Interpreting the model means describing the effect of one variable
at chosen values of the other, often after centering the variables so that zero is a typical value.

An interaction is not the same as the two variables being correlated with each other. Two independent
variables can be uncorrelated and still interact, and correlated variables need not interact at all.

### Common misconceptions

- An interaction means the two independent variables are correlated with each other.
- With an interaction in the model, the main-effect coefficient is still the average effect of that variable.
- A significant interaction means one variable causes the other.
