﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.GenerateFromMembers;
using Microsoft.CodeAnalysis.Internal.Log;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.AddConstructorParametersFromMembers
{
    internal partial class AddConstructorParametersFromMembersService : AbstractGenerateFromMembersService
    {
        public static readonly AddConstructorParametersFromMembersService Instance = new AddConstructorParametersFromMembersService();

        private AddConstructorParametersFromMembersService()
        {
        }

        public async Task<ImmutableArray<CodeAction>> AddConstructorParametersFromMembersAsync(Document document, TextSpan textSpan, CancellationToken cancellationToken)
        {
            using (Logger.LogBlock(FunctionId.Refactoring_GenerateFromMembers_AddConstructorParametersFromMembers, cancellationToken))
            {
                var info = await this.GetSelectedMemberInfoAsync(document, textSpan, cancellationToken).ConfigureAwait(false);
                if (info != null)
                {
                    var state = State.Generate(this, document, textSpan, info.SelectedMembers, cancellationToken);
                    if (state != null)
                    {
                        return CreateCodeActions(document, state).AsImmutableOrNull();
                    }
                }

                return default(ImmutableArray<CodeAction>);
            }
        }

        private IEnumerable<CodeAction> CreateCodeActions(Document document, State state)
        {
            var lastParameter = state.DelegatedConstructor.Parameters.Last();
            if (!lastParameter.IsOptional)
            {
                yield return new AddConstructorParametersCodeAction(this, document, state, state.Parameters);
            }

            var parameters = state.Parameters.Select(p => CodeGenerationSymbolFactory.CreateParameterSymbol(
                attributes: null,
                refKind: p.RefKind,
                isParams: p.IsParams,
                type: p.Type,
                name: p.Name,
                isOptional: true,
                hasDefaultValue: true)).ToList();

            yield return new AddConstructorParametersCodeAction(this, document, state, parameters);
        }
    }
}